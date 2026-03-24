using System.Runtime.Versioning;
using WinMirrorClicker;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var runAsTargetAgent = args.Any(a => string.Equals(a, "--target", StringComparison.OrdinalIgnoreCase));
        var initialTargetHwnd = ParseTargetHwnd(args);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(runAsTargetAgent, initialTargetHwnd));
    }

    private static IntPtr? ParseTargetHwnd(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--target-hwnd=", StringComparison.OrdinalIgnoreCase))
            {
                var value = a.Substring("--target-hwnd=".Length);
                if (TryParseHwnd(value, out var hwnd)) return hwnd;
            }
            else if (string.Equals(a, "--target-hwnd", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && TryParseHwnd(args[i + 1], out var hwnd)) return hwnd;
            }
        }

        return null;
    }

    private static bool TryParseHwnd(string value, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(value.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
            {
                hwnd = new IntPtr(hex);
                return hwnd != IntPtr.Zero;
            }

            return false;
        }

        if (long.TryParse(value, out var dec))
        {
            hwnd = new IntPtr(dec);
            return hwnd != IntPtr.Zero;
        }

        return false;
    }
}
