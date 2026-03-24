using System.Globalization;
using System.Text.RegularExpressions;

namespace WinMirrorClicker;

internal static class Config
{
    private static readonly object Gate = new();
    private static DateTime _lastWriteUtc;

    internal static int ClickDelayMs { get; private set; } = 100;
    internal static ClickSendMethod SendMethod { get; private set; } = ClickSendMethod.PostMessage;
    internal static ScaleMode Scale { get; private set; } = ScaleMode.Auto;
    internal static bool RestoreCursor { get; private set; } = true;
    internal static bool LogCoords { get; private set; }

    internal static void LoadIfChanged()
    {
        lock (Gate)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.txt");
            if (!File.Exists(path))
            {
                ClickDelayMs = 100;
                SendMethod = ClickSendMethod.PostMessage;
                _lastWriteUtc = default;
                return;
            }

            var writeUtc = File.GetLastWriteTimeUtc(path);
            if (writeUtc == _lastWriteUtc) return;
            _lastWriteUtc = writeUtc;

            var content = File.ReadAllText(path);
            if (TryParseDelay(content, out var delay))
            {
                ClickDelayMs = Math.Clamp(delay, 0, 60_000);
            }

            if (TryParseMethod(content, out var method))
            {
                SendMethod = method;
            }

            if (TryParseScale(content, out var scale))
            {
                Scale = scale;
            }

            if (TryParseRestoreCursor(content, out var restoreCursor))
            {
                RestoreCursor = restoreCursor;
            }

            if (TryParseLogCoords(content, out var logCoords))
            {
                LogCoords = logCoords;
            }
        }
    }

    private static bool TryParseDelay(string content, out int delay)
    {
        var match = Regex.Match(content, @"(?im)^\s*DELAY\s*=\s*(\d+)\s*$");
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out delay))
        {
            return true;
        }

        return int.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out delay);
    }

    private static bool TryParseMethod(string content, out ClickSendMethod method)
    {
        method = ClickSendMethod.PostMessage;

        var match = Regex.Match(content, @"(?im)^\s*(METHOD|SEND_METHOD)\s*=\s*([A-Z0-9_]+)\s*$");
        if (!match.Success) return false;

        var v = match.Groups[2].Value.Trim();
        if (string.Equals(v, "POSTMESSAGE", StringComparison.OrdinalIgnoreCase)) { method = ClickSendMethod.PostMessage; return true; }
        if (string.Equals(v, "SENDINPUT", StringComparison.OrdinalIgnoreCase)) { method = ClickSendMethod.SendInput; return true; }
        if (string.Equals(v, "INPUT", StringComparison.OrdinalIgnoreCase)) { method = ClickSendMethod.SendInput; return true; }

        return false;
    }

    private static bool TryParseScale(string content, out ScaleMode scale)
    {
        scale = ScaleMode.Auto;
        var match = Regex.Match(content, @"(?im)^\s*(SCALE|COORD_SCALE)\s*=\s*([A-Z0-9_]+)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        if (string.Equals(v, "AUTO", StringComparison.OrdinalIgnoreCase)) { scale = ScaleMode.Auto; return true; }
        if (string.Equals(v, "NONE", StringComparison.OrdinalIgnoreCase)) { scale = ScaleMode.None; return true; }
        return false;
    }

    private static bool TryParseRestoreCursor(string content, out bool restore)
    {
        restore = true;
        var match = Regex.Match(content, @"(?im)^\s*(RESTORE_CURSOR)\s*=\s*(true|false|1|0)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        restore = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        return true;
    }

    private static bool TryParseLogCoords(string content, out bool enabled)
    {
        enabled = false;
        var match = Regex.Match(content, @"(?im)^\s*(LOG_COORDS)\s*=\s*(true|false|1|0)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        enabled = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        return true;
    }
}
