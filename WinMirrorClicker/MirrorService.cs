using System.Drawing;

namespace WinMirrorClicker;

internal sealed class MirrorService
{
    internal Task MirrorLeftClickAsync(IntPtr sourceHwnd, IntPtr targetHwnd, Point screenPos, CancellationToken cancellationToken)
    {
        if (sourceHwnd == IntPtr.Zero || targetHwnd == IntPtr.Zero) return Task.CompletedTask;
        if (sourceHwnd == targetHwnd) return Task.CompletedTask;
        if (!NativeMethods.IsWindow(sourceHwnd) || !NativeMethods.IsWindow(targetHwnd)) return Task.CompletedTask;
        if (NativeMethods.GetForegroundWindow() != sourceHwnd) return Task.CompletedTask;

        Config.LoadIfChanged();

        if (!TryGetClientPoint(sourceHwnd, screenPos, out var clientPoint)) return Task.CompletedTask;

        TryGetClientSize(sourceHwnd, out var sourceW, out var sourceH);
        return MirrorClientLeftClickAsync(targetHwnd, clientPoint.X, clientPoint.Y, Config.ClickDelayMs, cancellationToken, restoreForeground: sourceHwnd, sourceClientW: sourceW, sourceClientH: sourceH);
    }

    internal Task MirrorClientLeftClickAsync(IntPtr targetHwnd, int clientX, int clientY, int delayMs, CancellationToken cancellationToken, IntPtr? restoreForeground = null, int? sourceClientW = null, int? sourceClientH = null)
    {
        if (targetHwnd == IntPtr.Zero) return Task.CompletedTask;
        if (!NativeMethods.IsWindow(targetHwnd)) return Task.CompletedTask;

        Config.LoadIfChanged();
        var (tx, ty) = ApplyScaling(clientX, clientY, targetHwnd, sourceClientW, sourceClientH);

        return Config.SendMethod switch
        {
            ClickSendMethod.SendInput => MirrorViaSendInputAsync(targetHwnd, tx, ty, delayMs, cancellationToken, restoreForeground),
            _ => MirrorViaPostMessageAsync(targetHwnd, tx, ty, delayMs, cancellationToken),
        };
    }

    internal static bool TryGetClientPoint(IntPtr hwnd, Point screenPos, out Point clientPoint)
    {
        clientPoint = default;
        if (hwnd == IntPtr.Zero) return false;

        var pt = new NativeMethods.POINT { x = screenPos.X, y = screenPos.Y };
        if (!NativeMethods.ScreenToClient(hwnd, ref pt)) return false;
        clientPoint = new Point(pt.x, pt.y);
        return true;
    }

    private static (int x, int y) ApplyScaling(int sourceClientX, int sourceClientY, IntPtr targetHwnd, int? sourceClientW, int? sourceClientH)
    {
        if (Config.Scale == ScaleMode.None) return (sourceClientX, sourceClientY);

        if (sourceClientW is null || sourceClientH is null) return (sourceClientX, sourceClientY);
        if (sourceClientW <= 0 || sourceClientH <= 0) return (sourceClientX, sourceClientY);

        if (!TryGetClientSize(targetHwnd, out var tgtW, out var tgtH)) return (sourceClientX, sourceClientY);
        if (tgtW <= 0 || tgtH <= 0) return (sourceClientX, sourceClientY);

        var x = (int)Math.Round(sourceClientX * (double)tgtW / sourceClientW.Value);
        var y = (int)Math.Round(sourceClientY * (double)tgtH / sourceClientH.Value);
        return (x, y);
    }

    private static bool TryGetClientSize(IntPtr hwnd, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (hwnd == IntPtr.Zero) return false;
        if (!NativeMethods.GetClientRect(hwnd, out var rect)) return false;
        width = rect.right - rect.left;
        height = rect.bottom - rect.top;
        return true;
    }

    private static async Task MirrorViaPostMessageAsync(IntPtr targetHwnd, int clientX, int clientY, int delayMs, CancellationToken cancellationToken)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        if (!NativeMethods.IsWindow(targetHwnd)) return;

        var lParam = NativeMethods.MakeLParam(clientX, clientY);
        NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_LBUTTONDOWN, (IntPtr)0x0001, lParam);
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_LBUTTONUP, IntPtr.Zero, lParam);
    }

    private static async Task MirrorViaSendInputAsync(IntPtr targetHwnd, int clientX, int clientY, int delayMs, CancellationToken cancellationToken, IntPtr? restoreForeground)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        if (!NativeMethods.IsWindow(targetHwnd)) return;

        var previousForeground = NativeMethods.GetForegroundWindow();
        var hasPrevCursor = NativeMethods.GetCursorPos(out var prevCursor);
        TryActivateWindow(targetHwnd);

        if (!TryClientToScreen(targetHwnd, clientX, clientY, out var screenX, out var screenY)) return;
        SendMouseClick(screenX, screenY);

        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        var restore = restoreForeground.GetValueOrDefault(previousForeground);
        if (restore != IntPtr.Zero && NativeMethods.IsWindow(restore))
        {
            TryActivateWindow(restore);
        }

        if (Config.RestoreCursor && hasPrevCursor)
        {
            RestoreCursor(prevCursor.x, prevCursor.y);
        }
    }

    private static bool TryClientToScreen(IntPtr hwnd, int clientX, int clientY, out int screenX, out int screenY)
    {
        var pt = new NativeMethods.POINT { x = clientX, y = clientY };
        if (!NativeMethods.ClientToScreen(hwnd, ref pt))
        {
            screenX = 0;
            screenY = 0;
            return false;
        }

        screenX = pt.x;
        screenY = pt.y;
        return true;
    }

    private static bool TryActivateWindow(IntPtr hwnd)
    {
        var currentThreadId = NativeMethods.GetCurrentThreadId();
        var targetThreadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (targetThreadId == 0) return false;

        var attached = false;
        try
        {
            attached = NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
            NativeMethods.SetForegroundWindow(hwnd);
            return true;
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }

    private static void SendMouseClick(int screenX, int screenY)
    {
        var vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (screenW <= 1 || screenH <= 1) return;

        var absX = (int)Math.Round((screenX - vx) * 65535.0 / (screenW - 1));
        var absY = (int)Math.Round((screenY - vy) * 65535.0 / (screenH - 1));

        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT { dx = absX, dy = absY, dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK }
                }
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT { dx = absX, dy = absY, dwFlags = NativeMethods.MOUSEEVENTF_LEFTDOWN | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK }
                }
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT { dx = absX, dy = absY, dwFlags = NativeMethods.MOUSEEVENTF_LEFTUP | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK }
                }
            }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void RestoreCursor(int screenX, int screenY)
    {
        var vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (screenW <= 1 || screenH <= 1) return;

        var absX = (int)Math.Round((screenX - vx) * 65535.0 / (screenW - 1));
        var absY = (int)Math.Round((screenY - vy) * 65535.0 / (screenH - 1));

        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT { dx = absX, dy = absY, dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK }
                }
            }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
