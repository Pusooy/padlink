# PadLink Windows 客户端（WinUI 3 分层架构）

参考 ProxyHotpot 的工程组织方式重构。

## 分层

```
windows/
├── Directory.Build.props            全局构建属性
├── pack-single.ps1                  单文件打包脚本
├── out/                             构建中间产物（可删）
└── src/
    ├── PadLink.Core/                net8.0 —— 配置/日志/存储（无 UI 依赖）
    │   ├── PadLinkConstants.cs      端口/路径/设置模型
    │   └── Storage.cs               Log / SettingsStore / AppsStore
    ├── PadLink.Interop/             net8.0-windows —— Win32 与系统服务
    │   ├── InputService.cs          SendInput 鼠标键盘
    │   ├── WindowService.cs         EnumWindows / 图标提取
    │   ├── ScreenService.cs         截屏 / 坐标映射 / 剪贴板 / 电源 / 系统信息
    │   ├── VolumeService.cs         音量控制 + 程序搜索（ProgramLauncher）
    │   ├── FileService.cs           文件浏览/预览/上传
    │   ├── AppLauncher.cs           应用启动 + UDP 广播发现
    │   ├── WebSocketHandler.cs      WS 协议分发
    │   └── WebHostBuilder.cs        Kestrel HTTP+WS 路由
    ├── PadLink.App.WinUI/           WinUI 3 界面（MVVM）
    │   ├── App.xaml(.cs)            单实例 / 托盘 / 服务启动
    │   ├── MainWindow.xaml(.cs)     NavigationView + 自定义标题栏
    │   ├── ViewModels/ShellViewModel.cs
    │   ├── Views/OverviewPage      概览
    │   ├── Views/AppsPage          应用管理
    │   ├── Views/ClientsPage       客户端与设置
    │   └── Services/               ServerHost / TrayIcon / StartupRegistration
    └── PadLink.Launcher/            单文件启动器（解压 payload 后启动 UI）
```

## 构建

```bash
# 开发构建
C:/Users/trump/.dotnet/dotnet.exe build windows/src/PadLink.App.WinUI/PadLink.App.csproj -c Release

# 单文件打包（约 3-5 分钟）
powershell -NoProfile -ExecutionPolicy Bypass -File windows/pack-single.ps1
# → dist/PadLink.exe（约 165 MB）
```

## 架构要点

- **UI 与系统服务同进程**，但严格分层：Core 无 Windows 依赖，Interop 只做 Win32/系统调用，WinUI 只做界面
- **单文件方案**：WinUI 3 无法真正单文件发布，因此 launcher 内嵌整个 UI 目录的 zip，
  首次运行解压到 `%LOCALAPPDATA%\PadLink\app`，版本变化时重新解压
- **单实例**：命名 Mutex + 事件唤回窗口
- **托盘**：手写 Shell_NotifyIcon（不用 WinForms NotifyIcon）
- **关闭窗口 = 隐藏到托盘**，只有托盘「退出」才真正退出
- **设置存储**：`%LOCALAPPDATA%\PadLink\settings.json`（apps.json 同目录）
