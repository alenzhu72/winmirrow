# WinMirror Clicker - D2R Follow

这是一个 Windows WinForms/.NET 8 小工具，用来辅助两个本地窗口之间的鼠标位置跟随。

默认用法面向《暗黑破坏神 II：重制版》双开：

- 工具窗口置顶悬浮在桌面。
- `F11`：绑定当前前台窗口为窗口1，也就是主控游戏窗口。
- `F12`：绑定当前前台窗口为窗口2。
- `Ctrl+Shift+E`：启动/停止跟随。启动后焦点会切回窗口1。
- 跟随开始后，程序会在窗口2里按住 `E`，并把窗口1内鼠标的同样客户区坐标同步到窗口2。
- `Ctrl+Shift+Z`：停止跟随，并释放按住的按键。

## 构建

用 Visual Studio 打开 `WinMirrorClicker.sln`，选择 Release/x64 构建即可。

也可以用命令行：

```powershell
dotnet build .\WinMirrorClicker.sln -c Release
```

输出位置：

```text
WinMirrorClicker\bin\Release\net8.0-windows\WinMirrorClicker.exe
```

## 推荐流程

1. 启动两个 D2R 窗口，并把两个窗口调整到相近大小。
2. 运行 `WinMirrorClicker.exe`。如果 D2R 是管理员权限运行，本工具也要用管理员权限运行。
3. 点一下第一个 D2R 窗口，让它成为前台窗口，按 `F11`。
4. 点一下第二个 D2R 窗口，让它成为前台窗口，按 `F12`。
5. 按 `Ctrl+Shift+E` 启动跟随，程序会把焦点切回窗口1。
6. 鼠标在窗口1内部移动时，窗口2会收到同样位置的鼠标移动，并保持 `E` 按住。
7. 要停止时按 `Ctrl+Shift+E` 或 `Ctrl+Shift+Z`。

## 配置

程序会从运行目录读取 `config.txt`。没有配置文件时，会使用 D2R 跟随默认值：

```txt
FOLLOW_MODE = 2
FORCED_MOVE_ENABLED = 1
FORCED_MOVE_VK = E
FORCED_MOVE_METHOD = POSTMESSAGE
FOLLOW_REQUIRE_SOURCE_FOREGROUND = 0
MOUSE_MOVE_INTERVAL_MS = 4
SCALE = NONE
```

如果游戏不响应后台 `PostMessage`，可以尝试：

```txt
FORCED_MOVE_METHOD = SENDINPUT
FORCED_MOVE_FOCUS_TARGET = 1
```

注意：`SENDINPUT` 会短暂切换前台和真实鼠标位置，体验取决于游戏、权限和系统焦点策略。

## 文件结构

- `WinMirrorClicker/MainForm.cs`：UI、全局热键、绑定和跟随逻辑。
- `WinMirrorClicker/GlobalHook.cs`：全局键鼠钩子。
- `WinMirrorClicker/MirrorService.cs`：坐标转换、鼠标移动和按键注入。
- `WinMirrorClicker/NativeMethods.cs`：Win32 API P/Invoke。
- `WinMirrorClicker/Config.cs`：配置文件解析和默认值。

## 注意

这个工具只做窗口级输入转发，不注入游戏进程、不读取游戏内存。不同游戏、不同权限级别、反作弊策略或全屏模式可能会导致输入不生效。建议使用窗口化或无边框窗口模式测试。
