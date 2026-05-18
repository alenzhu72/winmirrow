using System.Diagnostics;
using System.Drawing;

namespace D2RExactMirror;

public partial class Form1 : Form
{
    private const int MoveIntervalMs = 8;
    private const int HotkeyBindWindow1 = 101;
    private const int HotkeyBindWindow2 = 102;
    private const int HotkeyToggle = 103;
    private const int HotkeyStop = 104;
    private const int HotkeyCaptureWindow1Anchor = 105;
    private const int HotkeyCaptureWindow2Anchor = 106;
    private const int HotkeyEmergencyExit = 107;
    private const int HotkeyForceTargetKey = 108;
    private const int HotkeySwapLeader = 109;

    private readonly Label _window1Label = new() { AutoSize = true };
    private readonly Label _window2Label = new() { AutoSize = true };
    private readonly Label _stateLabel = new() { AutoSize = true };
    private readonly Label _coordLabel = new() { AutoSize = true };
    private readonly Label _anchorLabel = new() { AutoSize = true };
    private readonly Button _autoBindButton = new() { AutoSize = true, Text = "自动绑定D2R窗口" };
    private readonly Button _captureSourceAnchorButton = new() { AutoSize = true, Text = "记录窗口1人物点" };
    private readonly Button _captureTargetAnchorButton = new() { AutoSize = true, Text = "记录窗口2人物点" };
    private readonly Button _coordinateModeButton = new() { AutoSize = true };
    private readonly Button _anchorModeButton = new() { AutoSize = true };
    private readonly Button _sendMethodButton = new() { AutoSize = true };
    private readonly Button _forceKeyButton = new() { AutoSize = true };
    private readonly Button _swapButton = new() { AutoSize = true, Text = "切换带领" };
    private readonly Button _toggleButton = new() { AutoSize = true, Text = "启动" };
    private readonly Button _stopButton = new() { AutoSize = true, Text = "强制停止" };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = MoveIntervalMs };
    private readonly DirectionOverlayForm _sourceOverlay = new(Color.Lime);
    private readonly DirectionOverlayForm _targetOverlay = new(Color.DeepSkyBlue);

    private IntPtr _window1;
    private IntPtr _window2;
    private bool _running;
    private bool _targetKeyDown;
    private bool _forceTargetKeyDown;
    private bool _activeTargetKeyRequested;
    private Point? _lastSent;
    private long _lastSendTick;
    private Point? _sourceAnchor;
    private Point? _targetAnchor;
    private CoordinateMode _coordinateMode = CoordinateMode.LegacyAbsolute;
    private AnchorMode _anchorMode = AnchorMode.Manual;
    private MouseSendMethod _mouseSendMethod = MouseSendMethod.PostMessage;

    public Form1()
    {
        InitializeComponent();
        BuildUi();
        _timer.Tick += (_, _) => MirrorOneTick();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotkeys();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        StopMirror();
        _sourceOverlay.Close();
        _targetOverlay.Close();
        UnregisterHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HotkeyBindWindow1:
                    BindWindow1();
                    break;
                case HotkeyBindWindow2:
                    BindWindow2();
                    break;
                case HotkeyToggle:
                    ToggleMirror();
                    break;
                case HotkeyStop:
                    StopMirror();
                    break;
                case HotkeyCaptureWindow1Anchor:
                    CaptureAnchor(_window1, "窗口1", ref _sourceAnchor);
                    break;
                case HotkeyCaptureWindow2Anchor:
                    CaptureAnchor(_window2, "窗口2", ref _targetAnchor);
                    break;
                case HotkeyEmergencyExit:
                    EmergencyExit();
                    break;
                case HotkeyForceTargetKey:
                    ToggleForceTargetKey();
                    break;
                case HotkeySwapLeader:
                    SwapLeaderFollower();
                    break;
            }
        }

        base.WndProc(ref m);
    }

    private void BuildUi()
    {
        Text = "D2R Exact Mirror";
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 360);
        MinimumSize = new Size(560, 300);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            Text = "D2R跟随：LegacyAbsolute会按旧项目方式把窗口1鼠标客户区坐标按比例映射到窗口2；DirectionAnchor会按人物点方向映射。"
        }, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.Add(_autoBindButton);
        buttons.Controls.Add(_captureSourceAnchorButton);
        buttons.Controls.Add(_captureTargetAnchorButton);
        buttons.Controls.Add(_coordinateModeButton);
        buttons.Controls.Add(_anchorModeButton);
        buttons.Controls.Add(_sendMethodButton);
        buttons.Controls.Add(_forceKeyButton);
        buttons.Controls.Add(_swapButton);
        buttons.Controls.Add(_toggleButton);
        buttons.Controls.Add(_stopButton);
        root.Controls.Add(buttons, 0, 1);

        root.Controls.Add(MakeRow("窗口1:", _window1Label), 0, 2);
        root.Controls.Add(MakeRow("窗口2:", _window2Label), 0, 3);
        root.Controls.Add(MakeRow("状态:", _stateLabel), 0, 4);
        root.Controls.Add(MakeRow("人物点:", _anchorLabel), 0, 5);
        root.Controls.Add(MakeRow("坐标:", _coordLabel), 0, 6);
        root.Controls.Add(_log, 0, 7);
        Controls.Add(root);

        _autoBindButton.Click += (_, _) => AutoBindDiabloWindows();
        _captureSourceAnchorButton.Click += (_, _) => CaptureAnchor(_window1, "窗口1", ref _sourceAnchor);
        _captureTargetAnchorButton.Click += (_, _) => CaptureAnchor(_window2, "窗口2", ref _targetAnchor);
        _coordinateModeButton.Click += (_, _) => ToggleCoordinateMode();
        _anchorModeButton.Click += (_, _) => ToggleAnchorMode();
        _sendMethodButton.Click += (_, _) => ToggleSendMethod();
        _forceKeyButton.Click += (_, _) => ToggleForceTargetKey();
        _swapButton.Click += (_, _) => SwapLeaderFollower();
        _toggleButton.Click += (_, _) => ToggleMirror();
        _stopButton.Click += (_, _) => StopMirror();
        UpdateLabels();
    }

    private static Control MakeRow(string name, Control value)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        row.Controls.Add(new Label
        {
            Text = name,
            Width = 64,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 8, 3)
        });
        row.Controls.Add(value);
        return row;
    }

    private void RegisterHotkeys()
    {
        var ok1 = NativeMethods.RegisterHotKey(Handle, HotkeyBindWindow1, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F11);
        var ok2 = NativeMethods.RegisterHotKey(Handle, HotkeyBindWindow2, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F12);
        var okToggle = NativeMethods.RegisterHotKey(Handle, HotkeyToggle, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_E);
        var okStop = NativeMethods.RegisterHotKey(Handle, HotkeyStop, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_Z);
        var okAnchor1 = NativeMethods.RegisterHotKey(Handle, HotkeyCaptureWindow1Anchor, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_1);
        var okAnchor2 = NativeMethods.RegisterHotKey(Handle, HotkeyCaptureWindow2Anchor, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_2);
        var okEmergency = NativeMethods.RegisterHotKey(Handle, HotkeyEmergencyExit, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_Q);
        var okForceKey = NativeMethods.RegisterHotKey(Handle, HotkeyForceTargetKey, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_H);
        var okSwap = NativeMethods.RegisterHotKey(Handle, HotkeySwapLeader, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_S);
        Log(ok1 ? "F11热键注册成功。" : "F11热键注册失败，可用自动绑定按钮。");
        Log(ok2 ? "F12热键注册成功。" : "F12热键注册失败，可用自动绑定按钮。");
        Log(okToggle ? "Ctrl+Shift+E热键注册成功。" : "Ctrl+Shift+E热键注册失败，可用启动/停止按钮。");
        Log(okStop ? "Ctrl+Shift+Z热键注册成功。" : "Ctrl+Shift+Z热键注册失败，可用停止按钮。");
        Log(okAnchor1 ? "Ctrl+Shift+1热键注册成功。" : "Ctrl+Shift+1热键注册失败，可用记录窗口1人物点按钮。");
        Log(okAnchor2 ? "Ctrl+Shift+2热键注册成功。" : "Ctrl+Shift+2热键注册失败，可用记录窗口2人物点按钮。");
        Log(okEmergency ? "Ctrl+Shift+Q强制结束热键注册成功。" : "Ctrl+Shift+Q强制结束热键注册失败。");
        Log(okForceKey ? "Ctrl+Shift+H强制按住/释放跟随窗口E热键注册成功。" : "Ctrl+Shift+H热键注册失败，可用强制E按钮。");
        Log(okSwap ? "Ctrl+Shift+S切换带领窗口热键注册成功。" : "Ctrl+Shift+S热键注册失败，可用切换带领按钮。");
    }

    private void UnregisterHotkeys()
    {
        NativeMethods.UnregisterHotKey(Handle, HotkeyBindWindow1);
        NativeMethods.UnregisterHotKey(Handle, HotkeyBindWindow2);
        NativeMethods.UnregisterHotKey(Handle, HotkeyToggle);
        NativeMethods.UnregisterHotKey(Handle, HotkeyStop);
        NativeMethods.UnregisterHotKey(Handle, HotkeyCaptureWindow1Anchor);
        NativeMethods.UnregisterHotKey(Handle, HotkeyCaptureWindow2Anchor);
        NativeMethods.UnregisterHotKey(Handle, HotkeyEmergencyExit);
        NativeMethods.UnregisterHotKey(Handle, HotkeyForceTargetKey);
        NativeMethods.UnregisterHotKey(Handle, HotkeySwapLeader);
    }

    private void BindWindow1()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (!CanBind(hwnd, "窗口1")) return;
        if (hwnd == _window2)
        {
            Log("窗口1不能和窗口2是同一个窗口。");
            return;
        }

        _window1 = hwnd;
        Log("已绑定窗口1: " + FormatWindow(hwnd));
        UpdateLabels();
    }

    private void BindWindow2()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (!CanBind(hwnd, "窗口2")) return;
        if (hwnd == _window1)
        {
            Log("窗口2不能和窗口1是同一个窗口。");
            return;
        }

        _window2 = hwnd;
        Log("已绑定窗口2: " + FormatWindow(hwnd));
        UpdateLabels();
    }

    private bool CanBind(IntPtr hwnd, string name)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            Log($"{name}绑定失败：没有有效前台窗口。");
            return false;
        }

        if (hwnd == Handle)
        {
            Log($"{name}不能绑定到本工具窗口。先点击游戏窗口，再按热键。");
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId)
        {
            Log($"{name}不能绑定到本工具自身进程。");
            return false;
        }

        return true;
    }

    private void AutoBindDiabloWindows()
    {
        var windows = FindDiabloWindows();
        if (windows.Count < 2)
        {
            Log($"只找到 {windows.Count} 个 Diablo II: Resurrected 窗口，无法自动绑定两个窗口。");
            return;
        }

        _window1 = windows[0].Handle;
        _window2 = windows[1].Handle;
        Log("自动绑定窗口1: " + FormatWindow(_window1));
        Log("自动绑定窗口2: " + FormatWindow(_window2));
        UpdateLabels();
    }

    private static List<WindowCandidate> FindDiabloWindows()
    {
        var result = new List<WindowCandidate>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            var title = NativeMethods.GetWindowTitle(hwnd);
            if (!title.Contains("Diablo II: Resurrected", StringComparison.OrdinalIgnoreCase)) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return true;
            result.Add(new WindowCandidate(hwnd, title, rect.Left, rect.Top));
            return true;
        }, IntPtr.Zero);

        return result
            .OrderBy(w => w.Left)
            .ThenBy(w => w.Top)
            .ToList();
    }

    private void ToggleMirror()
    {
        if (_running)
        {
            StopMirror();
        }
        else
        {
            StartMirror();
        }
    }

    private void ToggleSendMethod()
    {
        _mouseSendMethod = _mouseSendMethod switch
        {
            MouseSendMethod.PostMessage => MouseSendMethod.SendMessage,
            MouseSendMethod.SendMessage => MouseSendMethod.FocusMessage,
            MouseSendMethod.FocusMessage => MouseSendMethod.FocusCursorPulse,
            _ => MouseSendMethod.PostMessage
        };
        Log($"鼠标发送方式: {_mouseSendMethod}");
        UpdateLabels();
    }

    private void ToggleAnchorMode()
    {
        _anchorMode = _anchorMode == AnchorMode.DynamicPlayCenter
            ? AnchorMode.Manual
            : AnchorMode.DynamicPlayCenter;
        Log($"人物点模式: {_anchorMode}");
        UpdateLabels();
    }

    private void ToggleCoordinateMode()
    {
        _coordinateMode = _coordinateMode == CoordinateMode.LegacyAbsolute
            ? CoordinateMode.DirectionAnchor
            : CoordinateMode.LegacyAbsolute;
        Log($"坐标模式: {_coordinateMode}");
        UpdateLabels();
    }

    private void ToggleForceTargetKey()
    {
        _forceTargetKeyDown = !_forceTargetKeyDown;
        ApplyTargetKeyState();
        Log(_forceTargetKeyDown ? "已强制按住跟随窗口 E。" : "已取消强制按住跟随窗口 E。");
        UpdateLabels();
    }

    private void SwapLeaderFollower()
    {
        if (_targetKeyDown)
        {
            PostTargetKey(false);
            _targetKeyDown = false;
        }

        (_window1, _window2) = (_window2, _window1);
        (_sourceAnchor, _targetAnchor) = (_targetAnchor, _sourceAnchor);
        _lastSent = null;
        _lastSendTick = 0;
        HideOverlays();
        ApplyTargetKeyState();
        Log("已切换带领窗口：当前窗口1为带领者，当前窗口2为跟随者，E发给当前窗口2。");
        UpdateLabels();
    }

    private void StartMirror()
    {
        if (!NativeMethods.IsWindow(_window1) || !NativeMethods.IsWindow(_window2))
        {
            AutoBindDiabloWindows();
        }

        if (!NativeMethods.IsWindow(_window1) || !NativeMethods.IsWindow(_window2))
        {
            Log("请先自动绑定，或用 F11 / F12 绑定两个游戏窗口。");
            return;
        }

        if (_coordinateMode == CoordinateMode.DirectionAnchor &&
            _anchorMode == AnchorMode.Manual &&
            (_sourceAnchor is null || _targetAnchor is null))
        {
            Log("Manual模式需要先标记人物位置：鼠标放到窗口1人物点按Ctrl+Shift+1，窗口2人物点按Ctrl+Shift+2。");
            UpdateLabels();
            return;
        }

        EnsureAnchorsForCurrentMode();
        _running = true;
        _lastSent = null;
        _lastSendTick = 0;
        RequestTargetKey(false);
        _timer.Start();
        Log("已启动：等待窗口1有效鼠标方向。");
        UpdateLabels();
    }

    private void CaptureAnchor(IntPtr hwnd, string name, ref Point? anchor)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            Log($"{name}未绑定，不能记录人物点。");
            return;
        }

        if (!NativeMethods.GetCursorPos(out var screenPoint))
        {
            return;
        }

        var clientPoint = screenPoint;
        if (!NativeMethods.ScreenToClient(hwnd, ref clientPoint))
        {
            return;
        }

        if (!NativeMethods.GetClientRect(hwnd, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (clientPoint.X < 0 || clientPoint.Y < 0 || clientPoint.X >= width || clientPoint.Y >= height)
        {
            Log($"{name}人物点记录失败：鼠标不在该窗口客户区内。");
            return;
        }

        anchor = new Point(clientPoint.X, clientPoint.Y);
        Log($"{name}人物点: ({clientPoint.X}, {clientPoint.Y})");
        UpdateLabels();
    }

    private void EmergencyExit()
    {
        try
        {
            _timer.Stop();
            _forceTargetKeyDown = false;
            _activeTargetKeyRequested = false;
            if (NativeMethods.IsWindow(_window2))
            {
                SetTargetKey(false);
            }
        }
        finally
        {
            Application.Exit();
        }
    }

    private void EnsureAnchorsForCurrentMode()
    {
        if (_anchorMode == AnchorMode.DynamicPlayCenter)
        {
            if (TryGetClientSize(_window1, out var sourceW, out var sourceH))
            {
                _sourceAnchor = GetDynamicPlayCenter(sourceW, sourceH);
            }

            if (TryGetClientSize(_window2, out var targetW, out var targetH))
            {
                _targetAnchor = GetDynamicPlayCenter(targetW, targetH);
            }

            UpdateLabels();
            return;
        }

        EnsureDefaultAnchors();
    }

    private void EnsureDefaultAnchors()
    {
        if (_sourceAnchor is null && TryGetClientSize(_window1, out var sourceW, out var sourceH))
        {
            _sourceAnchor = new Point(sourceW / 2, sourceH / 2);
            Log($"窗口1未记录人物点，临时使用中心点: ({_sourceAnchor.Value.X}, {_sourceAnchor.Value.Y})");
        }

        if (_targetAnchor is null && TryGetClientSize(_window2, out var targetW, out var targetH))
        {
            _targetAnchor = new Point(targetW / 2, targetH / 2);
            Log($"窗口2未记录人物点，临时使用中心点: ({_targetAnchor.Value.X}, {_targetAnchor.Value.Y})");
        }

        UpdateLabels();
    }

    private static Point GetDynamicPlayCenter(int width, int height)
    {
        return new Point(width / 2, (int)Math.Round(height * 0.42));
    }

    private void StopMirror()
    {
        _timer.Stop();
        HideOverlays();
        _forceTargetKeyDown = false;
        _activeTargetKeyRequested = false;
        if (_running && NativeMethods.IsWindow(_window2))
        {
            SetTargetKey(false);
        }

        _running = false;
        _lastSent = null;
        _lastSendTick = 0;
        Log("已停止：窗口2释放 E。");
        UpdateLabels();
    }

    private void MirrorOneTick()
    {
        if (!_running) return;
        if (!NativeMethods.IsWindow(_window1) || !NativeMethods.IsWindow(_window2))
        {
            StopMirror();
            return;
        }

        if (NativeMethods.GetForegroundWindow() != _window1)
        {
            _coordLabel.Text = "窗口1不是焦点，不发送";
            HideOverlays();
            RequestTargetKey(false);
            return;
        }

        if (!NativeMethods.GetCursorPos(out var screenPoint))
        {
            RequestTargetKey(false);
            return;
        }

        if (!IsPointVisiblyOnWindow(_window1, screenPoint))
        {
            _coordLabel.Text = "鼠标不在窗口1可见区域，不发送";
            HideOverlays();
            RequestTargetKey(false);
            return;
        }

        var clientPoint = screenPoint;
        if (!NativeMethods.ScreenToClient(_window1, ref clientPoint))
        {
            RequestTargetKey(false);
            return;
        }

        if (!NativeMethods.GetClientRect(_window1, out var sourceRect) ||
            !NativeMethods.GetClientRect(_window2, out var targetRect))
        {
            RequestTargetKey(false);
            return;
        }

        var sourceW = sourceRect.Right - sourceRect.Left;
        var sourceH = sourceRect.Bottom - sourceRect.Top;
        var targetW = targetRect.Right - targetRect.Left;
        var targetH = targetRect.Bottom - targetRect.Top;
        if (sourceW <= 0 || sourceH <= 0 || targetW <= 0 || targetH <= 0)
        {
            RequestTargetKey(false);
            return;
        }

        var x = clientPoint.X;
        var y = clientPoint.Y;
        if (x < 0 || y < 0 || x >= sourceW || y >= sourceH)
        {
            _coordLabel.Text = $"窗口1外: ({x}, {y})";
            RequestTargetKey(false);
            return;
        }

        var scaleX = targetW / (double)sourceW;
        var scaleY = targetH / (double)sourceH;
        int targetX;
        int targetY;
        string coordText;

        if (_coordinateMode == CoordinateMode.LegacyAbsolute)
        {
            targetX = Math.Clamp((int)Math.Round(x * (targetW - 1.0) / Math.Max(1, sourceW - 1)), 0, targetW - 1);
            targetY = Math.Clamp((int)Math.Round(y * (targetH - 1.0) / Math.Max(1, sourceH - 1)), 0, targetH - 1);
            coordText = $"Legacy ({x}, {y})/{sourceW}x{sourceH} -> 窗口2 ({targetX}, {targetY})/{targetW}x{targetH}";
        }
        else
        {
            EnsureAnchorsForCurrentMode();
            if (_sourceAnchor is null || _targetAnchor is null)
            {
                RequestTargetKey(false);
                return;
            }

            targetX = Math.Clamp((int)Math.Round(_targetAnchor.Value.X + (x - _sourceAnchor.Value.X) * scaleX), 0, targetW - 1);
            targetY = Math.Clamp((int)Math.Round(_targetAnchor.Value.Y + (y - _sourceAnchor.Value.Y) * scaleY), 0, targetH - 1);
            coordText = $"方向 ({x - _sourceAnchor.Value.X}, {y - _sourceAnchor.Value.Y}) -> 窗口2 ({targetX}, {targetY})";
        }

        var point = new Point(targetX, targetY);
        var now = Environment.TickCount64;
        var resendMs = _mouseSendMethod == MouseSendMethod.FocusCursorPulse ? 180 : 80;
        if (_lastSent == point && now - _lastSendTick < resendMs)
        {
            if (_mouseSendMethod != MouseSendMethod.FocusCursorPulse)
            {
                RequestTargetKey(true);
            }
            return;
        }
        _lastSent = point;
        _lastSendTick = now;

        var lParam = NativeMethods.MakeLParam(targetX, targetY);
        SendTargetMouseMove(targetX, targetY, lParam);
        if (_mouseSendMethod != MouseSendMethod.FocusMessage && _mouseSendMethod != MouseSendMethod.FocusCursorPulse)
        {
            RequestTargetKey(true);
        }
        var sourceOverlayAnchor = _coordinateMode == CoordinateMode.LegacyAbsolute ? new Point(x, y) : _sourceAnchor;
        var targetOverlayAnchor = _coordinateMode == CoordinateMode.LegacyAbsolute ? new Point(targetX, targetY) : _targetAnchor;
        _sourceOverlay.UpdateOverlay(_window1, sourceOverlayAnchor, new Point(x, y));
        _targetOverlay.UpdateOverlay(_window2, targetOverlayAnchor, new Point(targetX, targetY));
        _coordLabel.Text = coordText;
    }

    private void HideOverlays()
    {
        _sourceOverlay.Hide();
        _targetOverlay.Hide();
    }

    private void SendTargetMouseMove(int targetX, int targetY, IntPtr lParam)
    {
        switch (_mouseSendMethod)
        {
            case MouseSendMethod.SendMessage:
                SendMouseMoveMessage(lParam);
                break;
            case MouseSendMethod.FocusMessage:
                SendFocusedMouseMoveMessage(lParam);
                break;
            case MouseSendMethod.FocusCursorPulse:
                SendFocusCursorPulse(targetX, targetY);
                break;
            default:
                NativeMethods.PostMessage(_window2, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, lParam);
                break;
        }
    }

    private void SendMouseMoveMessage(IntPtr lParam)
    {
        NativeMethods.SendMessageTimeout(
            _window2,
            NativeMethods.WM_MOUSEMOVE,
            IntPtr.Zero,
            lParam,
            NativeMethods.SMTO_ABORTIFHUNG,
            20,
            out _);
    }

    private void SendFocusedMouseMoveMessage(IntPtr lParam)
    {
        var previous = NativeMethods.GetForegroundWindow();
        NativeMethods.SetForegroundWindow(_window2);
        Thread.Sleep(1);
        SendMouseMoveMessage(lParam);
        PostTargetKey(true);
        _targetKeyDown = true;

        if (previous != IntPtr.Zero && previous != _window2 && NativeMethods.IsWindow(previous))
        {
            Thread.Sleep(1);
            NativeMethods.SetForegroundWindow(previous);
        }
    }

    private void SendFocusCursorPulse(int targetX, int targetY)
    {
        if (!NativeMethods.GetCursorPos(out var originalCursor)) return;

        var targetPoint = new NativeMethods.POINT { X = targetX, Y = targetY };
        if (!NativeMethods.ClientToScreen(_window2, ref targetPoint)) return;

        var previous = NativeMethods.GetForegroundWindow();
        NativeMethods.SetForegroundWindow(_window2);
        Thread.Sleep(2);
        SendAbsoluteMouseMove(targetPoint.X, targetPoint.Y);
        Thread.Sleep(2);
        SendInputKey(NativeMethods.VK_E, down: true);
        Thread.Sleep(25);
        SendInputKey(NativeMethods.VK_E, down: false);
        _targetKeyDown = false;

        if (previous != IntPtr.Zero && previous != _window2 && NativeMethods.IsWindow(previous))
        {
            NativeMethods.SetForegroundWindow(previous);
            Thread.Sleep(1);
        }

        SendAbsoluteMouseMove(originalCursor.X, originalCursor.Y);
    }

    private static void SendAbsoluteMouseMove(int screenX, int screenY)
    {
        var vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (vw <= 1 || vh <= 1) return;

        var absX = (int)Math.Round((screenX - vx) * 65535.0 / (vw - 1));
        var absY = (int)Math.Round((screenY - vy) * 65535.0 / (vh - 1));
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK
                }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendInputKey(uint vk, bool down)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP
                }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private void RequestTargetKey(bool down)
    {
        _activeTargetKeyRequested = down;
        ApplyTargetKeyState();
    }

    private void SetTargetKey(bool down)
    {
        _activeTargetKeyRequested = down;
        _forceTargetKeyDown = false;
        ApplyTargetKeyState();
    }

    private void ApplyTargetKeyState()
    {
        var desired = _activeTargetKeyRequested || _forceTargetKeyDown;
        if (_targetKeyDown == desired) return;
        _targetKeyDown = desired;
        PostTargetKey(desired);
        UpdateLabels();
    }

    private void PostTargetKey(bool down)
    {
        NativeMethods.PostMessage(_window2, down ? NativeMethods.WM_KEYDOWN : NativeMethods.WM_KEYUP, (IntPtr)NativeMethods.VK_E, IntPtr.Zero);
    }

    private static bool IsPointVisiblyOnWindow(IntPtr expectedWindow, NativeMethods.POINT screenPoint)
    {
        var pointWindow = NativeMethods.WindowFromPoint(screenPoint);
        if (pointWindow == IntPtr.Zero) return false;

        var root = NativeMethods.GetAncestor(pointWindow, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero) root = pointWindow;
        return root == expectedWindow;
    }

    private static bool TryGetClientSize(IntPtr hwnd, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!NativeMethods.IsWindow(hwnd)) return false;
        if (!NativeMethods.GetClientRect(hwnd, out var rect)) return false;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    private void UpdateLabels()
    {
        _window1Label.Text = _window1 == IntPtr.Zero ? "未绑定" : $"带领者  {FormatWindow(_window1)}";
        _window2Label.Text = _window2 == IntPtr.Zero ? "未绑定" : $"跟随者/E目标  {FormatWindow(_window2)}";
        var eMode = _forceTargetKeyDown ? "强制按下" : (_targetKeyDown ? "按下" : "松开");
        _stateLabel.Text = _running ? $"运行中，跟随者E={eMode}，鼠标={_mouseSendMethod}" : $"停止，跟随者E={eMode}，鼠标={_mouseSendMethod}";
        _anchorLabel.Text = $"坐标={_coordinateMode}  人物点={_anchorMode}  窗口1={FormatPoint(_sourceAnchor)}  窗口2={FormatPoint(_targetAnchor)}";
        _coordinateModeButton.Text = $"坐标:{_coordinateMode}";
        _anchorModeButton.Text = $"人物点:{_anchorMode}";
        _sendMethodButton.Text = $"鼠标:{_mouseSendMethod}";
        _forceKeyButton.Text = _forceTargetKeyDown ? "取消强制E" : "强制E";
        _toggleButton.Text = _running ? "停止" : "启动";
    }

    private static string FormatPoint(Point? point)
    {
        return point is { } p ? $"({p.X},{p.Y})" : "未记录";
    }

    private static string FormatWindow(IntPtr hwnd)
    {
        var title = NativeMethods.GetWindowTitle(hwnd);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var hex = "0x" + hwnd.ToInt64().ToString("X");
        return string.IsNullOrWhiteSpace(title)
            ? $"{hex} PID={pid}"
            : $"{hex} PID={pid} {title}";
    }

    private void Log(string message)
    {
        if (!IsHandleCreated) return;
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }

    private sealed record WindowCandidate(IntPtr Handle, string Title, int Left, int Top);

    private enum CoordinateMode
    {
        LegacyAbsolute,
        DirectionAnchor
    }

    private enum MouseSendMethod
    {
        PostMessage,
        SendMessage,
        FocusMessage,
        FocusCursorPulse
    }

    private enum AnchorMode
    {
        DynamicPlayCenter,
        Manual
    }
}

