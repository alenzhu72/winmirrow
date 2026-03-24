using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMirrorClicker;

internal static class TargetAgentLauncher
{
    internal static bool TrySpawnTargetAgentForWindow(IntPtr targetHwnd, out string message)
    {
        message = string.Empty;
        if (targetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(targetHwnd))
        {
            message = "窗口2句柄无效。";
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(targetHwnd, out var pid);
        if (pid == 0)
        {
            message = "无法获取窗口2所属进程 PID。";
            return false;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            message = "无法获取当前程序路径。";
            return false;
        }

        var cmdLine = $"\"{exePath}\" --target --target-hwnd 0x{targetHwnd.ToInt64():X}";

        var procHandle = OpenProcess(PROCESS_QUERY_INFORMATION, inheritHandle: false, (int)pid);
        if (procHandle == IntPtr.Zero)
        {
            message = $"OpenProcess 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        try
        {
            if (!OpenProcessToken(procHandle, TOKEN_DUPLICATE | TOKEN_QUERY, out var hToken))
            {
                message = $"OpenProcessToken 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                return false;
            }

            try
            {
                if (!DuplicateTokenEx(
                        hToken,
                        TOKEN_ALL_ACCESS,
                        IntPtr.Zero,
                        SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                        TOKEN_TYPE.TokenPrimary,
                        out var hPrimaryToken))
                {
                    message = $"DuplicateTokenEx 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    return false;
                }

                try
                {
                    var si = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        lpDesktop = @"winsta0\default"
                    };

                    if (!CreateProcessWithTokenW(
                            hPrimaryToken,
                            LOGON_WITH_PROFILE,
                            lpApplicationName: null,
                            lpCommandLine: cmdLine,
                            dwCreationFlags: 0,
                            lpEnvironment: IntPtr.Zero,
                            lpCurrentDirectory: AppContext.BaseDirectory,
                            ref si,
                            out var pi))
                    {
                        message = $"CreateProcessWithTokenW 失败: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                        return false;
                    }

                    try
                    {
                        message = $"已尝试以窗口2所属用户启动目标代理。PID={pi.dwProcessId}";
                        return true;
                    }
                    finally
                    {
                        if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                        if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                    }
                }
                finally
                {
                    CloseHandle(hPrimaryToken);
                }
            }
            finally
            {
                CloseHandle(hToken);
            }
        }
        finally
        {
            CloseHandle(procHandle);
        }
    }

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;

    private const uint LOGON_WITH_PROFILE = 0x00000001;

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2
    }

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool inheritHandle, int dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
        TOKEN_TYPE TokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr hToken,
        uint dwLogonFlags,
        string? lpApplicationName,
        string lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

