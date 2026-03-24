# WinMirror Clicker

WinMirror Clicker 是一个 Windows 实用工具，用于在指定“窗口1(源)”捕获鼠标左键点击，并在可配置的延迟后，将同一相对坐标的点击镜像到“窗口2(目标)”。

## 功能
- F9 绑定当前前台窗口为窗口1(源)
- F10 绑定当前前台窗口为窗口2(目标)
- F11 互换窗口1与窗口2的绑定
- Ctrl+Shift+Z 终止跟随（关闭镜像），再次开启可按 F12 或通过界面切换
- 支持从 `config.txt` 动态读取延迟与模式，无需重启
- 支持跨用户/跨会话：在目标用户会话中运行 `--target` 代理，源端通过命名管道发送坐标
- 两窗口尺寸不同的情况下自动按客户区比例缩放坐标
- 真实鼠标注入模式（SendInput），适配对 `PostMessage` 不响应的游戏
- 点击后自动恢复焦点到窗口1，并可恢复光标位置，尽量不打断你的操作

## 快速开始
1. 使用 .NET 8（Windows）构建解决方案：
   ```bash
   dotnet build .\WinMirrorClicker.sln -c Release
   ```
2. 运行源端（默认模式）：
   ```bash
   .\WinMirrorClicker\bin\Release\net8.0-windows\WinMirrorClicker.exe
   ```
3. 在“窗口1(源)”所在用户会话中，激活窗口并按 F9 绑定；然后激活“窗口2(目标)”窗口并按 F10 绑定。
4. 运行目标代理（当窗口2在另一个本地用户/会话中时）：
   ```bash
   .\WinMirrorClicker\bin\Release\net8.0-windows\WinMirrorClicker.exe --target
   ```
   - 如果无法登录目标用户但窗口已运行，可在源端绑定好窗口2后点击界面上的“从窗口2进程启动目标代理”，程序会使用该进程的令牌在目标会话中拉起 `--target`。
   - 也可带上 `--target-hwnd` 让目标代理自动绑定指定窗口，如：
     ```bash
     .\WinMirrorClicker.exe --target --target-hwnd 0x1234ABCD
     ```

## 配置文件
程序从运行目录的 `config.txt` 读取配置，启动时会在日志中打印该文件的完整路径。

示例：
```txt
DELAY = 80
METHOD = SENDINPUT
SCALE = AUTO
RESTORE_CURSOR = 1
LOG_COORDS = 0
```

- DELAY：点击镜像延迟，单位毫秒
- METHOD：`POSTMESSAGE` 或 `SENDINPUT`（推荐）
- SCALE：`AUTO` 按客户区尺寸线性缩放；`NONE` 不缩放
- RESTORE_CURSOR：是否在目标点击后恢复光标位置（SendInput 模式下）
- LOG_COORDS：打印源/目标坐标方向与位移，用于排查偏移问题

## 使用注意
- 如果目标程序以管理员权限运行，WinMirror Clicker 也需要以管理员运行，否则可能被 Windows UIPI 拦截导致输入无效
- 有些游戏对 `PostMessage(WM_LBUTTONDOWN/UP)` 不响应，建议改为 `METHOD=SENDINPUT`
- 多显示器情况下使用虚拟屏坐标进行注入，建议将两个窗口放在同一显示器上测试定位问题
- 源/目标不能绑定到同一个窗口，程序会提示并拒绝绑定

## 热键一览
- F9：绑定窗口1(源)
- F10：绑定窗口2(目标)
- F11：互换源/目标
- Ctrl+Shift+Z：终止跟随（关闭镜像）

## 目录结构
- WinMirrorClicker：WinForms 工程（.NET 8）
  - Program.cs：入口
  - MainForm.cs：UI 与绑定逻辑、热键、日志
  - GlobalHook.cs：键鼠钩子
  - MirrorService.cs：坐标转换与点击镜像（PostMessage/SendInput）
  - NativeMethods.cs：Win32 API P/Invoke
  - Ipc.cs：命名管道通信（源端/目标代理）
  - Config.cs：配置加载
  - app.manifest：应用清单
  - WinMirrorClicker.csproj：工程文件

## 构建与运行
```bash
dotnet build .\WinMirrorClicker.sln -c Release
.\WinMirrorClicker\bin\Release\net8.0-windows\WinMirrorClicker.exe
```

## 许可证
建议根据你的诉求选择开源许可证（例如 MIT）。目前未附带 LICENSE 文件。
