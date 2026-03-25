using System.Drawing;
using System.Diagnostics;
using System.Globalization;

namespace WinMirrorClicker;

internal sealed class MainForm : Form
{
    private readonly bool _runAsTargetAgent;
    private readonly IntPtr? _initialTargetHwnd;

    private readonly Label _sourceLabel = new() { AutoSize = true };
    private readonly Label _targetLabel = new() { AutoSize = true };
    private readonly Label _delayLabel = new() { AutoSize = true };
    private readonly Label _modeLabel = new() { AutoSize = true };
    private readonly Label _pipeLabel = new() { AutoSize = true };
    private readonly Label _followLabel = new() { AutoSize = true };
    private readonly Button _toggleFollowButton = new() { AutoSize = true, Text = "切换跟随" };
    private readonly Button _launchTargetAgentButton = new() { AutoSize = true, Text = "启动目标代理(--target)" };
    private readonly Button _spawnTargetAgentButton = new() { AutoSize = true, Text = "从窗口2进程启动目标代理" };
    private readonly Panel _sourceStatus = new() { Width = 16, Height = 16, BackColor = Color.DarkRed, Margin = new Padding(0, 2, 8, 2) };
    private readonly Panel _targetStatus = new() { Width = 16, Height = 16, BackColor = Color.DarkRed, Margin = new Padding(0, 2, 8, 2) };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    private readonly MirrorService _mirror = new();
    private readonly GlobalHook _hook;
    private readonly CancellationTokenSource _cts = new();
    private SourceAgentClient? _client;
    private TargetAgentServer? _server;
    private Task? _serverTask;

    private IntPtr _sourceHwnd;
    private IntPtr _targetHwnd;
    private bool _hotkeysRegistered;
    private long _lastBindTick;
    private Point? _lastSourceClientPoint;
    private Point? _lastTargetClientPoint;
    private bool _followEnabled = true;
    private bool _forcedMovePressed;

    private const int HotkeyIdBindSource = 1;
    private const int HotkeyIdBindTarget = 2;
    private const int HotkeyIdSwap = 3;
    private const int HotkeyIdStopFollow = 4;
    private const int HotkeyIdMasterToggle = 5;

    internal MainForm(bool runAsTargetAgent, IntPtr? initialTargetHwnd)
    {
        _runAsTargetAgent = runAsTargetAgent;
        _initialTargetHwnd = initialTargetHwnd;
        _hook = new GlobalHook(enableKeyboard: true);

        Text = "WinMirror Clicker";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "F9 绑定窗口1(源) / F10 绑定窗口2(目标)。在窗口1内左键点击后，将在延迟后把相对坐标点击发送到窗口2。",
            AutoSize = true,
            MaximumSize = new Size(900, 0),
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildRuntimeRow(), 0, 1);
        root.Controls.Add(BuildBindRow("窗口1(源)", _sourceStatus, _sourceLabel), 0, 2);
        root.Controls.Add(BuildBindRow("窗口2(目标)", _targetStatus, _targetLabel), 0, 3);

        var logHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        logHeader.Controls.Add(new Label { Text = "当前延迟:", AutoSize = true, Margin = new Padding(0, 3, 6, 3) });
        logHeader.Controls.Add(_delayLabel);
        root.Controls.Add(logHeader, 0, 4);

        var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 110 };
        main.Panel1.Controls.Add(root);
        main.Panel2.Controls.Add(_log);
        Controls.Add(main);

        Shown += (_, _) => InitializeRuntime();
        FormClosing += (_, _) => Shutdown();

        _spawnTargetAgentButton.Click += (_, _) => SpawnTargetAgentFromTargetWindow();
        _toggleFollowButton.Click += (_, _) => ToggleFollow();
        _launchTargetAgentButton.Click += (_, _) => LaunchTargetAgentInCurrentSession();
    }

    private static Control BuildBindRow(string caption, Control status, Control value)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = caption + ":", AutoSize = true, Width = 110, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 6, 3) });
        row.Controls.Add(status);
        row.Controls.Add(value);
        return row;
    }

    private Control BuildRuntimeRow()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _modeLabel.Text = _runAsTargetAgent ? "模式: 目标代理 (--target)" : "模式: 源端 (默认)";
        _pipeLabel.Text = $"管道: {IpcDefaults.PipeName}";
        row.Controls.Add(_modeLabel);
        row.Controls.Add(new Label { Text = "   ", AutoSize = true });
        row.Controls.Add(_pipeLabel);
        row.Controls.Add(new Label { Text = "   ", AutoSize = true });
        _followLabel.Text = "跟随: 开(Ctrl+Shift+E开/关, Ctrl+Shift+Z停止)";
        row.Controls.Add(_followLabel);
        row.Controls.Add(_toggleFollowButton);
        if (!_runAsTargetAgent)
        {
            row.Controls.Add(new Label { Text = "   ", AutoSize = true });
            row.Controls.Add(_launchTargetAgentButton);
            row.Controls.Add(_spawnTargetAgentButton);
        }
        return row;
    }

    private void InitializeRuntime()
    {
        try
        {
            Config.LoadIfChanged();
            UpdateDelayLabel();

            _hook.KeyDown += OnGlobalKeyDownFallback;
            _hook.KeyUp += OnGlobalKeyUpFallback;
            _hook.MouseLeftDown += OnGlobalMouseLeftDown;
            _hook.MouseMove += OnGlobalMouseMove;
            _hook.Start();

            AppendLog("全局钩子已启用。");
            AppendLog($"当前进程 Session={Process.GetCurrentProcess().SessionId}");
            AppendLog($"配置文件: {Path.Combine(AppContext.BaseDirectory, "config.txt")}");
            RegisterHotkeys();

            if (_runAsTargetAgent)
            {
                _server = new TargetAgentServer(IpcDefaults.PipeName);
                _server.CommandReceived += OnIpcCommand;
                _serverTask = Task.Run(() => _server.RunAsync(_cts.Token), _cts.Token);
                AppendLog("目标代理已启动，等待源端连接。");

                if (_initialTargetHwnd is { } initial && initial != IntPtr.Zero)
                {
                    BindTarget(initial);
                }
            }
            else
            {
                _client = new SourceAgentClient(IpcDefaults.PipeName);
                _ = Task.Run(async () =>
                {
                    var ok = await _client.EnsureConnectedAsync(_cts.Token).ConfigureAwait(false);
                    AppendLog(ok ? "已连接到目标代理。" : "未连接到目标代理（如果需要跨用户窗口，请在目标用户下以 --target 启动本程序）。");
                }, _cts.Token);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void Shutdown()
    {
        _cts.Cancel();
        _hook.Dispose();
        UnregisterHotkeys();
        _client?.Dispose();
        _server?.Dispose();
        _cts.Dispose();
    }

    private void OnGlobalKeyDownFallback(int vkCode)
    {
        if (Config.ForcedMoveEnabled && vkCode == Config.ForcedMoveVk)
        {
            _forcedMovePressed = true;
            AppendLog("源端强制移动键已按下。");
            var target = _targetHwnd;
            var source = _sourceHwnd;
            if (target != IntPtr.Zero && source != IntPtr.Zero)
            {
                _ = MirrorService.SendForcedKeyAsync(target, Config.ForcedMoveVk, true, source, _cts.Token);
            }
            return;
        }

        if (vkCode != NativeMethods.VK_F9 && vkCode != NativeMethods.VK_F10) return;
        if (!IsHandleCreated) return;
        if (_runAsTargetAgent && vkCode == NativeMethods.VK_F9) return;

        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastBindTick);
        if (last != 0)
        {
            var deltaMs = (now - last) * 1000.0 / Stopwatch.Frequency;
            if (deltaMs < 80) return;
        }
        Interlocked.Exchange(ref _lastBindTick, now);

        var hwnd = NativeMethods.GetForegroundWindow();
        BeginInvoke(new Action(() =>
        {
            if (vkCode == NativeMethods.VK_F9) BindSource(hwnd);
            else BindTarget(hwnd);
        }));
    }

    private void OnGlobalKeyUpFallback(int vkCode)
    {
        if (!IsHandleCreated) return;
        if (Config.ForcedMoveEnabled && vkCode == Config.ForcedMoveVk)
        {
            _forcedMovePressed = false;
            AppendLog("源端强制移动键已抬起。");
            var target = _targetHwnd;
            var source = _sourceHwnd;
            if (target != IntPtr.Zero && source != IntPtr.Zero)
            {
                _ = MirrorService.SendForcedKeyAsync(target, Config.ForcedMoveVk, false, source, _cts.Token);
            }
        }
    }

    private void OnGlobalMouseLeftDown(int x, int y)
    {
        if (_runAsTargetAgent) return;
        if (!_followEnabled) return;

        var source = _sourceHwnd;
        if (source == IntPtr.Zero) return;
        if (NativeMethods.GetForegroundWindow() != source) return;

        Config.LoadIfChanged();
        var delayMs = Config.ClickDelayMs;

        if (!MirrorService.TryGetClientPoint(source, new Point(x, y), out var clientPoint)) return;

        if (Config.LogCoords)
        {
            LogCoordDelta("SRC", clientPoint, ref _lastSourceClientPoint);
        }

        if (_client is not null && _client.IsConnected)
        {
            var sourceW = 0;
            var sourceH = 0;
            if (NativeMethods.GetClientRect(source, out var rect))
            {
                sourceW = rect.right - rect.left;
                sourceH = rect.bottom - rect.top;
            }

            _ = _client.SendAsync(new MirrorClickCommand(clientPoint.X, clientPoint.Y, delayMs, sourceW, sourceH), _cts.Token);
            return;
        }

        var target = _targetHwnd;
        if (target == IntPtr.Zero || target == source) return;
        var srcW = 0;
        var srcH = 0;
        if (NativeMethods.GetClientRect(source, out var srcRect))
        {
            srcW = srcRect.right - srcRect.left;
            srcH = srcRect.bottom - srcRect.top;
        }

        if (Config.LogCoords)
        {
            var tgtPoint = ApplyScalingLocal(clientPoint, srcW, srcH, target);
            LogCoordDelta("TGT", tgtPoint, ref _lastTargetClientPoint);
        }

        _ = _mirror.MirrorClientLeftClickAsync(target, clientPoint.X, clientPoint.Y, delayMs, _cts.Token, sourceClientW: srcW, sourceClientH: srcH);
    }

    private void OnGlobalMouseMove(int x, int y)
    {
        if (_runAsTargetAgent) return;
        if (!_followEnabled) return;
        if (Config.ForcedMoveEnabled)
        {
            if (_forcedMovePressed == false && KeyboardForcedMoveIsPressed())
            {
                _forcedMovePressed = true;
                AppendLog("源端强制移动键已按下。");
            }

            if (_forcedMovePressed)
            {
                var source = _sourceHwnd;
                var target = _targetHwnd;
                if (source == IntPtr.Zero || target == IntPtr.Zero) return;
                _ = _mirror.MirrorForcedMoveAsync(source, target, new Point(x, y), _cts.Token);
            }
        }
    }

    private bool KeyboardForcedMoveIsPressed()
    {
        return false;
    }

    private void OnIpcCommand(MirrorClickCommand cmd)
    {
        var target = _targetHwnd;
        if (target == IntPtr.Zero) return;
        var srcW = cmd.SourceClientW > 0 ? cmd.SourceClientW : (int?)null;
        var srcH = cmd.SourceClientH > 0 ? cmd.SourceClientH : (int?)null;
        if (Config.LogCoords)
        {
            var srcPoint = new Point(cmd.ClientX, cmd.ClientY);
            LogCoordDelta("SRC", srcPoint, ref _lastSourceClientPoint);
            if (srcW is { } w && srcH is { } h)
            {
                var tgtPoint = ApplyScalingLocal(srcPoint, w, h, target);
                LogCoordDelta("TGT", tgtPoint, ref _lastTargetClientPoint);
            }
        }
        _ = _mirror.MirrorClientLeftClickAsync(target, cmd.ClientX, cmd.ClientY, cmd.DelayMs, _cts.Token, sourceClientW: srcW, sourceClientH: srcH);
    }

    private void UpdateBindLabels()
    {
        _sourceLabel.Text = _sourceHwnd == IntPtr.Zero ? "(未绑定)" : FormatWindow(_sourceHwnd);
        _targetLabel.Text = _targetHwnd == IntPtr.Zero ? "(未绑定)" : FormatWindow(_targetHwnd);
        _sourceStatus.BackColor = _sourceHwnd == IntPtr.Zero ? Color.DarkRed : Color.DarkGreen;
        _targetStatus.BackColor = _targetHwnd == IntPtr.Zero ? Color.DarkRed : Color.DarkGreen;
        UpdateDelayLabel();
    }

    private static string FormatWindow(IntPtr hwnd)
    {
        var title = NativeMethods.GetWindowTitle(hwnd);
        var hex = "0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        var pidText = string.Empty;
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                var session = Process.GetProcessById((int)pid).SessionId;
                pidText = $"  PID={pid}  Session={session}";
            }
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(title) ? $"{hex}{pidText}" : $"{hex}{pidText}  {title}";
    }

    private void UpdateDelayLabel()
    {
        Config.LoadIfChanged();
        _delayLabel.Text = $"{Config.ClickDelayMs} ms  {Config.SendMethod}  {Config.Scale}  Burst={Config.BurstCount}x/{Config.BurstIntervalMs}ms  RestoreCursor={Config.RestoreCursor}  LogCoords={Config.LogCoords} (config.txt)";
    }

    private static Point ApplyScalingLocal(Point srcClientPoint, int srcW, int srcH, IntPtr targetHwnd)
    {
        if (Config.Scale == ScaleMode.None) return srcClientPoint;
        if (srcW <= 0 || srcH <= 0) return srcClientPoint;
        if (!NativeMethods.GetClientRect(targetHwnd, out var rect)) return srcClientPoint;
        var tgtW = rect.right - rect.left;
        var tgtH = rect.bottom - rect.top;
        if (tgtW <= 0 || tgtH <= 0) return srcClientPoint;
        var x = (int)Math.Round(srcClientPoint.X * (double)tgtW / srcW);
        var y = (int)Math.Round(srcClientPoint.Y * (double)tgtH / srcH);
        return new Point(x, y);
    }

    private void LogCoordDelta(string tag, Point current, ref Point? last)
    {
        if (last is { } prev)
        {
            var dx = current.X - prev.X;
            var dy = current.Y - prev.Y;
            var dir = GetDirection(dx, dy);
            AppendLog($"{tag} client=({current.X},{current.Y}) Δ=({dx},{dy}) dir={dir}");
        }
        else
        {
            AppendLog($"{tag} client=({current.X},{current.Y})");
        }

        last = current;
    }

    private static string GetDirection(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return "None";
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return dx < 0 ? "Left" : "Right";
        }

        return dy < 0 ? "Up" : "Down";
    }

    private void AppendLog(string line)
    {
        if (!IsHandleCreated) return;
        var text = $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}";
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => _log.AppendText(text)));
        }
        else
        {
            _log.AppendText(text);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (id == HotkeyIdBindSource)
            {
                if (!_runAsTargetAgent) BindSourceFromForeground();
            }
            else if (id == HotkeyIdBindTarget)
            {
                BindTargetFromForeground();
            }
            else if (id == HotkeyIdSwap)
            {
                SwapSourceTarget();
            }
            else if (id == HotkeyIdStopFollow)
            {
                StopFollow();
            }
            else if (id == HotkeyIdMasterToggle)
            {
                ToggleFollow();
            }
        }

        base.WndProc(ref m);
    }

    private void RegisterHotkeys()
    {
        if (_hotkeysRegistered) return;
        if (!IsHandleCreated) return;

        var okTarget = NativeMethods.RegisterHotKey(Handle, HotkeyIdBindTarget, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F10);
        var okSource = _runAsTargetAgent || NativeMethods.RegisterHotKey(Handle, HotkeyIdBindSource, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F9);
        var okSwap = NativeMethods.RegisterHotKey(Handle, HotkeyIdSwap, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F11);
        var okStop = NativeMethods.RegisterHotKey(Handle, HotkeyIdStopFollow, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_Z);
        var okMaster = NativeMethods.RegisterHotKey(Handle, HotkeyIdMasterToggle, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, (uint)Config.ForcedMoveVk);

        _hotkeysRegistered = okTarget && okSource && okSwap && okStop && okMaster;
        AppendLog(_hotkeysRegistered ? "已注册全局热键(F9/F10/F11 + Ctrl+Shift+E开/关 + Ctrl+Shift+Z停止)。" : "注册全局热键失败（可能被其他程序占用）。");
    }

    private void UnregisterHotkeys()
    {
        if (!_hotkeysRegistered) return;
        if (!IsHandleCreated) return;

        NativeMethods.UnregisterHotKey(Handle, HotkeyIdBindTarget);
        NativeMethods.UnregisterHotKey(Handle, HotkeyIdBindSource);
        NativeMethods.UnregisterHotKey(Handle, HotkeyIdSwap);
        NativeMethods.UnregisterHotKey(Handle, HotkeyIdStopFollow);
        NativeMethods.UnregisterHotKey(Handle, HotkeyIdMasterToggle);
        _hotkeysRegistered = false;
    }

    private void BindSourceFromForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        BindSource(hwnd);
    }

    private void BindTargetFromForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        BindTarget(hwnd);
    }

    private void BindSource(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (hwnd == Handle)
        {
            AppendLog("窗口1(源) 不能绑定到本工具窗口。请先激活你的游戏窗口再按 F9。");
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Process.GetCurrentProcess().Id)
        {
            AppendLog("窗口1(源) 不能绑定到当前进程窗口。请先激活你的游戏窗口再按 F9。");
            return;
        }

        if (_targetHwnd != IntPtr.Zero && hwnd == _targetHwnd)
        {
            AppendLog("窗口1(源) 与 窗口2(目标) 不能是同一个窗口。请重新激活正确的窗口再按 F9。");
            return;
        }

        _sourceHwnd = hwnd;
        UpdateBindLabels();
        AppendLog($"已绑定窗口1(源): {FormatWindow(hwnd)}");
    }

    private void BindTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (hwnd == Handle)
        {
            AppendLog("窗口2(目标) 不能绑定到本工具窗口。请先激活你的目标窗口再按 F10。");
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Process.GetCurrentProcess().Id)
        {
            AppendLog("窗口2(目标) 不能绑定到当前进程窗口。请先激活你的目标窗口再按 F10。");
            return;
        }

        if (_sourceHwnd != IntPtr.Zero && hwnd == _sourceHwnd)
        {
            AppendLog("窗口2(目标) 与 窗口1(源) 不能是同一个窗口。请重新激活正确的窗口再按 F10。");
            return;
        }

        _targetHwnd = hwnd;
        UpdateBindLabels();
        AppendLog($"已绑定窗口2(目标): {FormatWindow(hwnd)}");
    }

    private void SpawnTargetAgentFromTargetWindow()
    {
        var hwnd = _targetHwnd;
        if (hwnd == IntPtr.Zero)
        {
            var auto = TryFindDiabloWindow();
            if (auto != IntPtr.Zero)
            {
                BindTarget(auto);
                hwnd = _targetHwnd;
            }
        }

        if (hwnd == IntPtr.Zero)
        {
            AppendLog("未绑定窗口2，且无法自动找到目标窗口。若目标窗口在另一个会话(Session)内，需要在那个会话运行 --target。");
            return;
        }

        var ok = TargetAgentLauncher.TrySpawnTargetAgentForWindow(hwnd, out var msg);
        AppendLog(msg);
        if (!ok)
        {
            AppendLog("提示：如果窗口2属于另一个用户且权限更高，通常需要以管理员身份运行本工具才能启动目标代理。");
        }
    }

    private void LaunchTargetAgentInCurrentSession()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                AppendLog("无法获取当前程序路径，启动目标代理失败。");
                return;
            }

            var args = "--target";
            if (_targetHwnd != IntPtr.Zero)
            {
                args += $" --target-hwnd 0x{_targetHwnd.ToInt64():X}";
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            var p = Process.Start(psi);
            AppendLog(p is null ? "启动目标代理失败（Process.Start 返回空）。" : $"已启动目标代理进程。PID={p.Id}");
        }
        catch (Exception ex)
        {
            AppendLog($"启动目标代理失败：{ex.Message}");
        }
    }

    private static IntPtr TryFindDiabloWindow()
    {
        var candidates = new[] { "Diablo II", "Resurrected", "Diablo II: Resurrected" };
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            var len = NativeMethods.GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            if (len > 512) len = 512;
            var buffer = new char[len + 1];
            var got = NativeMethods.GetWindowText(hWnd, buffer, buffer.Length);
            if (got <= 0) return true;
            var title = new string(buffer, 0, got);
            for (var i = 0; i < candidates.Length; i++)
            {
                if (title.Contains(candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private void SwapSourceTarget()
    {
        var a = _sourceHwnd;
        var b = _targetHwnd;
        _sourceHwnd = b;
        _targetHwnd = a;
        UpdateBindLabels();
        AppendLog("已互换窗口1(源)与窗口2(目标)绑定。");
    }

    private void ToggleFollow()
    {
        _followEnabled = !_followEnabled;
        _followLabel.Text = _followEnabled ? "跟随: 开(Ctrl+Shift+E开/关, Ctrl+Shift+Z停止)" : "跟随: 关(Ctrl+Shift+E开/关, Ctrl+Shift+Z停止)";
        AppendLog(_followEnabled ? "已开启跟随。" : "已关闭跟随。");

        if (!_followEnabled && _forcedMovePressed)
        {
            _forcedMovePressed = false;
            var target = _targetHwnd;
            var source = _sourceHwnd;
            if (target != IntPtr.Zero && source != IntPtr.Zero)
            {
                _ = MirrorService.SendForcedKeyAsync(target, Config.ForcedMoveVk, false, source, _cts.Token);
            }
        }
    }

    private void StopFollow()
    {
        if (!_followEnabled) return;
        _followEnabled = false;
        _followLabel.Text = "跟随: 关(Ctrl+Shift+E开/关, Ctrl+Shift+Z停止)";
        AppendLog("已关闭跟随。");
    }
}
