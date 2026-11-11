# -*- coding: utf-8 -*-
import io

P = (r"C:\Users\trump\.zcode\cli\memories\projects"
     r"\40c7b29c-1556-4b3b-ad16-4ac9f6367c84-8b8417c80882ffc3\memory"
     r"\padlink-artifacts.md")

content = """---
name: padlink-artifacts
description: PadLink 各客户端单文件产物路径 —— 用户明确要求每次交付都要主动告知这些路径
metadata:
  type: project
---

用户明确要求：**以后每次交付 PadLink 改动，都要主动列出各客户端单文件产物的位置。**

## 产物清单（2026-11-11 更新）

| 客户端 | 单文件产物 | 大小 |
|---|---|---|
| **Windows 客户端** | `C:\\Users\\trump\\PadLink\\PadLink.exe` | ~165 MB（单文件 launcher + 内嵌 payload） |
| 构建输出 | `padlink\\dist\\PadLink.exe` | 同上 |
| **安卓 App** | `C:\\Users\\trump\\Downloads\\sidestore-setup\\PadLink.apk` | ~3.6 MB |
| **网页 PWA** | 无需文件，由 Windows 客户端内嵌提供 | 手机访问 `http://<PC-IP>:8765` |

## Windows 架构（WinUI 3 分层，参考 ProxyHotpot）

```
padlink/windows/
├── pack-single.ps1          单文件打包脚本
├── out/                     中间产物（gitignore）
└── src/
    ├── PadLink.Core/        net8.0：常量/日志/设置/apps.json 存储
    ├── PadLink.Interop/     net8.0-windows：Win32 + 系统服务（输入/窗口/截屏/音量/文件/WS/HTTP）
    ├── PadLink.App.WinUI/   WinUI 3 界面（MVVM：ShellViewModel + 3 页面）
    └── PadLink.Launcher/    单文件启动器（内嵌 payload.zip，解压到 %LOCALAPPDATA%\\PadLink\\app）
```

**WinUI 3 无法真正单文件发布**，所以用 launcher + payload 方案：
- launcher（单文件 exe）内嵌整个 UI 目录的 zip
- 首次运行解压到 `%LOCALAPPDATA%\\PadLink\\app`，比较 version.txt 决定是否重解压
- 设置/日志/apps.json 在 `%LOCALAPPDATA%\\PadLink\\`

## 构建命令

```bash
# 开发构建
C:/Users/trump/.dotnet/dotnet.exe build windows/src/PadLink.App.WinUI/PadLink.App.csproj -c Release

# 单文件打包（3-5 分钟）
powershell -NoProfile -ExecutionPolicy Bypass -File windows/pack-single.ps1
# → dist/PadLink.exe

# 安卓
# 触发：POST /repos/Pusooy/padlink/actions/workflows/build-apk.yml/dispatches {"ref":"main"}
```

## 环境要点

- **用 `C:\\Users\\trump\\.dotnet\\dotnet.exe`**（用户目录 SDK），不是系统 dotnet
- WinUI 3 需要的包：Microsoft.WindowsAppSDK 1.8.260921001 + Windows.SDK.BuildTools 10.0.26100.9457（已缓存）
- 首次还原依赖约 7 分钟
- 打包前必须 `taskkill //IM PadLink.UI.exe //F`，否则文件锁定导致编译失败
- PowerShell 脚本**用英文**（中文在 GBK 环境下会被当注释/报错）

相关：[[padlink-project]]、[[user-environment-facts]]
"""

io.open(P, "w", encoding="utf-8").write(content)
print("产物清单已更新")
