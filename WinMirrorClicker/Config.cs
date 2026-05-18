using System.Globalization;
using System.Text.RegularExpressions;

namespace WinMirrorClicker;

internal static class Config
{
    private static readonly object Gate = new();
    private static DateTime _lastWriteUtc;

    internal static int ClickDelayMs { get; private set; } = 100;
    internal static ClickSendMethod SendMethod { get; private set; } = ClickSendMethod.PostMessage;
    internal static ScaleMode Scale { get; private set; } = ScaleMode.None;
    internal static bool RestoreCursor { get; private set; } = true;
    internal static bool LogCoords { get; private set; }
    internal static int BurstCount { get; private set; } = 1;
    internal static int BurstIntervalMs { get; private set; } = 30;
    internal static bool ForcedMoveEnabled { get; private set; } = true;
    internal static int ForcedMoveVk { get; private set; } = NativeMethods.VK_E;
    internal static ClickSendMethod ForcedMoveSendMethod { get; private set; } = ClickSendMethod.PostMessage;
    internal static bool ForcedMoveFocusTarget { get; private set; }
    internal static bool FollowRequireSourceForeground { get; private set; }
    internal static int FollowMode { get; private set; } = 2;
    internal static int MouseMoveIntervalMs { get; private set; } = 4;

    internal static void LoadIfChanged()
    {
        lock (Gate)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.txt");
            if (!File.Exists(path))
            {
                ClickDelayMs = 100;
                SendMethod = ClickSendMethod.PostMessage;
                Scale = ScaleMode.None;
                RestoreCursor = true;
                LogCoords = false;
                BurstCount = 1;
                BurstIntervalMs = 30;
                ForcedMoveEnabled = true;
                ForcedMoveVk = NativeMethods.VK_E;
                ForcedMoveSendMethod = ClickSendMethod.PostMessage;
                ForcedMoveFocusTarget = false;
                FollowRequireSourceForeground = false;
                FollowMode = 2;
                MouseMoveIntervalMs = 4;
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

            if (TryParseBurstCount(content, out var burstCount))
            {
                BurstCount = Math.Clamp(burstCount, 1, 10);
            }

            if (TryParseBurstInterval(content, out var burstIntervalMs))
            {
                BurstIntervalMs = Math.Clamp(burstIntervalMs, 0, 500);
            }

            if (TryParseForcedMoveEnabled(content, out var forcedEnabled))
            {
                ForcedMoveEnabled = forcedEnabled;
            }

            if (TryParseForcedMoveVk(content, out var forcedVk))
            {
                ForcedMoveVk = forcedVk;
            }

            if (TryParseForcedMoveMethod(content, out var forcedMethod))
            {
                ForcedMoveSendMethod = forcedMethod;
            }

            if (TryParseForcedMoveFocusTarget(content, out var focusTarget))
            {
                ForcedMoveFocusTarget = focusTarget;
            }

            if (TryParseFollowRequireSourceForeground(content, out var requireFg))
            {
                FollowRequireSourceForeground = requireFg;
            }

            if (TryParseFollowMode(content, out var followMode))
            {
                FollowMode = followMode;
            }

            if (TryParseMouseMoveIntervalMs(content, out var moveIntervalMs))
            {
                MouseMoveIntervalMs = Math.Clamp(moveIntervalMs, 0, 100);
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

    private static bool TryParseBurstCount(string content, out int count)
    {
        count = 1;
        var match = Regex.Match(content, @"(?im)^\s*(BURST_COUNT|CLICK_BURST)\s*=\s*(\d+)\s*$");
        if (!match.Success) return false;
        return int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
    }

    private static bool TryParseBurstInterval(string content, out int intervalMs)
    {
        intervalMs = 30;
        var match = Regex.Match(content, @"(?im)^\s*(BURST_INTERVAL_MS|CLICK_BURST_INTERVAL_MS)\s*=\s*(\d+)\s*$");
        if (!match.Success) return false;
        return int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intervalMs);
    }

    private static bool TryParseForcedMoveEnabled(string content, out bool enabled)
    {
        enabled = false;
        var match = Regex.Match(content, @"(?im)^\s*(FORCED_MOVE_ENABLED)\s*=\s*(true|false|1|0)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        enabled = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        return true;
    }

    private static bool TryParseForcedMoveVk(string content, out int vk)
    {
        vk = NativeMethods.VK_E;
        var match = Regex.Match(content, @"(?im)^\s*(FORCED_MOVE_VK)\s*=\s*([A-Za-z0-9_]+)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        if (v.Length == 1)
        {
            var c = char.ToUpperInvariant(v[0]);
            if (c >= 'A' && c <= 'Z') { vk = (int)c; return true; }
        }
        if (string.Equals(v, "E", StringComparison.OrdinalIgnoreCase)) { vk = NativeMethods.VK_E; return true; }
        if (string.Equals(v, "Z", StringComparison.OrdinalIgnoreCase)) { vk = NativeMethods.VK_Z; return true; }
        return false;
    }

    private static bool TryParseForcedMoveMethod(string content, out ClickSendMethod method)
    {
        method = ClickSendMethod.PostMessage;
        var match = Regex.Match(content, @"(?im)^\s*(FORCED_MOVE_METHOD|FORCED_MOVE_SEND_METHOD)\s*=\s*([A-Z0-9_]+)\s*$");
        if (!match.Success) return false;

        var v = match.Groups[2].Value.Trim();
        if (string.Equals(v, "POSTMESSAGE", StringComparison.OrdinalIgnoreCase)) { method = ClickSendMethod.PostMessage; return true; }
        if (string.Equals(v, "SENDINPUT", StringComparison.OrdinalIgnoreCase)) { method = ClickSendMethod.SendInput; return true; }
        if (string.Equals(v, "INPUT", StringComparison.OrdinalIgnoreCase)) { method = ClickSendMethod.SendInput; return true; }
        return false;
    }

    private static bool TryParseForcedMoveFocusTarget(string content, out bool enabled)
    {
        enabled = false;
        var match = Regex.Match(content, @"(?im)^\s*(FORCED_MOVE_FOCUS_TARGET|FOCUS_TARGET)\s*=\s*(true|false|1|0)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        enabled = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        return true;
    }

    private static bool TryParseFollowRequireSourceForeground(string content, out bool enabled)
    {
        enabled = false;
        var match = Regex.Match(content, @"(?im)^\s*(FOLLOW_REQUIRE_SOURCE_FOREGROUND|REQUIRE_SOURCE_FOREGROUND)\s*=\s*(true|false|1|0)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        enabled = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        return true;
    }

    private static bool TryParseFollowMode(string content, out int mode)
    {
        mode = 1;
        var match = Regex.Match(content, @"(?im)^\s*(FOLLOW_MODE)\s*=\s*([A-Z0-9_]+)\s*$");
        if (!match.Success) return false;
        var v = match.Groups[2].Value.Trim();
        if (v == "1" || string.Equals(v, "MODE1", StringComparison.OrdinalIgnoreCase)) { mode = 1; return true; }
        if (v == "2" || string.Equals(v, "MODE2", StringComparison.OrdinalIgnoreCase)) { mode = 2; return true; }
        return false;
    }

    private static bool TryParseMouseMoveIntervalMs(string content, out int intervalMs)
    {
        intervalMs = 8;
        var match = Regex.Match(content, @"(?im)^\s*(MOUSE_MOVE_INTERVAL_MS|MOVE_INTERVAL_MS)\s*=\s*(\d+)\s*$");
        if (!match.Success) return false;
        return int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intervalMs);
    }
}
