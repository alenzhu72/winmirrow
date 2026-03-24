using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMirrorClicker;

internal sealed class GlobalHook : IDisposable
{
    private readonly NativeMethods.HookProc _mouseProc;
    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly bool _enableKeyboard;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _started;

    internal event Action<int>? KeyDown;
    internal event Action<int, int>? MouseLeftDown;

    internal GlobalHook(bool enableKeyboard = true)
    {
        _enableKeyboard = enableKeyboard;
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    internal void Start()
    {
        if (_started) return;
        _started = true;

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var hMod = NativeMethods.GetModuleHandle(moduleName);

        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_enableKeyboard)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        }

        if (_mouseHook == IntPtr.Zero || (_enableKeyboard && _keyboardHook == IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            Stop();
            throw new InvalidOperationException($"SetWindowsHookEx failed. Win32Error={error}");
        }
    }

    private void Stop()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        _started = false;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            MouseLeftDown?.Invoke(data.pt.x, data.pt.y);
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (!_enableKeyboard)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            KeyDown?.Invoke((int)data.vkCode);
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
