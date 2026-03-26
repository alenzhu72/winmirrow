using System.Drawing;

namespace WinMirrorClicker;

internal sealed class MirrorService
{
    internal static void PostKeyAsync(IntPtr targetHwnd, int vk, bool down)
    {
        if (!NativeMethods.IsWindow(targetHwnd)) return;
        var msg = down ? NativeMethods.WM_KEYDOWN : NativeMethods.WM_KEYUP;
        NativeMethods.PostMessage(targetHwnd, msg, (IntPtr)vk, IntPtr.Zero);
    }

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

    internal Task MirrorForcedMoveAsync(IntPtr sourceHwnd, IntPtr targetHwnd, Point screenPos, CancellationToken cancellationToken)
    {
        Config.LoadIfChanged();
        if (!Config.ForcedMoveEnabled) return Task.CompletedTask;
        if (sourceHwnd == IntPtr.Zero || targetHwnd == IntPtr.Zero) return Task.CompletedTask;
        if (Config.FollowRequireSourceForeground && NativeMethods.GetForegroundWindow() != sourceHwnd) return Task.CompletedTask;
        if (!TryGetClientPointClamped(sourceHwnd, screenPos, out var clientPoint, out var sourceW, out var sourceH)) return Task.CompletedTask;
        var (tx, ty) = ApplyScaling(clientPoint.X, clientPoint.Y, targetHwnd, sourceW, sourceH);
        return MirrorMoveCoreAsync(sourceHwnd, targetHwnd, tx, ty, cancellationToken);
    }

    internal Task MirrorMouseMoveAsync(IntPtr sourceHwnd, IntPtr targetHwnd, Point screenPos, CancellationToken cancellationToken)
    {
        Config.LoadIfChanged();
        if (sourceHwnd == IntPtr.Zero || targetHwnd == IntPtr.Zero) return Task.CompletedTask;
        if (Config.FollowMode != 2 && Config.FollowRequireSourceForeground && NativeMethods.GetForegroundWindow() != sourceHwnd) return Task.CompletedTask;
        if (!TryGetClientPointClamped(sourceHwnd, screenPos, out var clientPoint, out var sourceW, out var sourceH)) return Task.CompletedTask;
        var (tx, ty) = ApplyScaling(clientPoint.X, clientPoint.Y, targetHwnd, sourceW, sourceH);
        return MirrorMoveCoreAsync(sourceHwnd, targetHwnd, tx, ty, cancellationToken);
    }

    internal Task MirrorMouseMovePostMessageAsync(IntPtr sourceHwnd, IntPtr targetHwnd, Point screenPos)
    {
        if (sourceHwnd == IntPtr.Zero || targetHwnd == IntPtr.Zero) return Task.CompletedTask;
        if (!NativeMethods.IsWindow(sourceHwnd) || !NativeMethods.IsWindow(targetHwnd)) return Task.CompletedTask;

        Config.LoadIfChanged();
        if (!TryGetClientPointClamped(sourceHwnd, screenPos, out var clientPoint, out var sourceW, out var sourceH)) return Task.CompletedTask;
        var (tx, ty) = ApplyScaling(clientPoint.X, clientPoint.Y, targetHwnd, sourceW, sourceH);

        var lParam = NativeMethods.MakeLParam(tx, ty);
        NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        return Task.CompletedTask;
    }

    private static Task MirrorMoveCoreAsync(IntPtr sourceHwnd, IntPtr targetHwnd, int targetClientX, int targetClientY, CancellationToken cancellationToken)
    {
        var method = Config.ForcedMoveSendMethod;
        if (method == ClickSendMethod.SendInput && !Config.ForcedMoveFocusTarget)
        {
            method = ClickSendMethod.PostMessage;
        }

        return method switch
        {
            ClickSendMethod.SendInput => MirrorForcedMoveCoreAsync(targetHwnd, targetClientX, targetClientY, cancellationToken, restoreForeground: sourceHwnd, focusTarget: true),
            _ => MirrorForcedMoveViaPostMessageAsync(targetHwnd, targetClientX, targetClientY),
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

    private static bool TryGetClientPointInBounds(IntPtr hwnd, Point screenPos, out Point clientPoint)
    {
        clientPoint = default;
        if (!TryGetClientPoint(hwnd, screenPos, out var pt)) return false;
        if (!TryGetClientSize(hwnd, out var w, out var h)) return false;
        if (pt.X < 0 || pt.Y < 0 || pt.X >= w || pt.Y >= h) return false;
        clientPoint = pt;
        return true;
    }

    private static bool TryGetClientPointClamped(IntPtr hwnd, Point screenPos, out Point clientPoint, out int clientW, out int clientH)
    {
        clientPoint = default;
        clientW = 0;
        clientH = 0;
        if (!TryGetClientPoint(hwnd, screenPos, out var pt)) return false;
        if (!TryGetClientSize(hwnd, out clientW, out clientH)) return false;
        if (clientW <= 0 || clientH <= 0) return false;
        var x = Math.Clamp(pt.X, 0, clientW - 1);
        var y = Math.Clamp(pt.Y, 0, clientH - 1);
        clientPoint = new Point(x, y);
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
        var burstCount = Config.BurstCount;
        var burstIntervalMs = Config.BurstIntervalMs;

        for (var i = 0; i < burstCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.IsWindow(targetHwnd)) return;

            NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_LBUTTONDOWN, (IntPtr)0x0001, lParam);
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_LBUTTONUP, IntPtr.Zero, lParam);

            if (burstIntervalMs > 0 && i < burstCount - 1)
            {
                await Task.Delay(burstIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
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
        var burstCount = Config.BurstCount;
        var burstIntervalMs = Config.BurstIntervalMs;
        for (var i = 0; i < burstCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.IsWindow(targetHwnd)) return;
            SendMouseClick(screenX, screenY);
            if (burstIntervalMs > 0 && i < burstCount - 1)
            {
                await Task.Delay(burstIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

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

    private static Task MirrorForcedMoveViaPostMessageAsync(IntPtr targetHwnd, int clientX, int clientY)
    {
        if (!NativeMethods.IsWindow(targetHwnd)) return Task.CompletedTask;
        var lParam = NativeMethods.MakeLParam(clientX, clientY);
        NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        return Task.CompletedTask;
    }

    private static async Task MirrorForcedMoveCoreAsync(IntPtr targetHwnd, int clientX, int clientY, CancellationToken cancellationToken, IntPtr? restoreForeground, bool focusTarget)
    {
        if (!NativeMethods.IsWindow(targetHwnd)) return;
        var previousForeground = NativeMethods.GetForegroundWindow();
        var hasPrevCursor = NativeMethods.GetCursorPos(out var prevCursor);
        if (focusTarget)
        {
            TryActivateWindow(targetHwnd);
        }
        if (!TryClientToScreen(targetHwnd, clientX, clientY, out var screenX, out var screenY)) return;
        SendMouseMoveOnly(screenX, screenY);
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        var restore = restoreForeground.GetValueOrDefault(previousForeground);
        if (focusTarget && restore != IntPtr.Zero && NativeMethods.IsWindow(restore)) TryActivateWindow(restore);
        if (Config.RestoreCursor && hasPrevCursor) RestoreCursor(prevCursor.x, prevCursor.y);
    }

    private static void SendMouseMoveOnly(int screenX, int screenY)
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

    internal static async Task SendForcedKeyAsync(IntPtr targetHwnd, int vk, bool down, IntPtr? restoreForeground, CancellationToken cancellationToken)
    {
        if (!NativeMethods.IsWindow(targetHwnd)) return;
        Config.LoadIfChanged();
        var method = Config.ForcedMoveSendMethod;
        if (method == ClickSendMethod.SendInput && !Config.ForcedMoveFocusTarget)
        {
            method = ClickSendMethod.PostMessage;
        }

        if (method == ClickSendMethod.PostMessage)
        {
            var msg = down ? NativeMethods.WM_KEYDOWN : NativeMethods.WM_KEYUP;
            NativeMethods.PostMessage(targetHwnd, msg, (IntPtr)vk, IntPtr.Zero);
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            return;
        }

        var previousForeground = NativeMethods.GetForegroundWindow();
        TryActivateWindow(targetHwnd);
        var input = new NativeMethods.INPUT
        {
            type = 1,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vk, wScan = 0, dwFlags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        var restore = restoreForeground.GetValueOrDefault(previousForeground);
        if (restore != IntPtr.Zero && NativeMethods.IsWindow(restore)) TryActivateWindow(restore);
    }
}
