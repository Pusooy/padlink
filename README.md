# PadLink —— 把手机变成 Windows 的无线触控板 + 键盘 + Dock

纯网页方案：iPhone/Android 浏览器打开即用，无需安装任何 App、无需签名、无需 Mac。
Windows 端是 **.NET 8 + WinForms 托盘程序**（`app/`），现代 Win11 原生 UI。
（`windows-server/` 为早期 Python 版本，功能相同，留作备用。）

```
手机浏览器/主屏幕App ──WebSocket──> PadLink 托盘程序（Kestrel, 端口 8765）
                                      ├─ SendInput 控制鼠标/键盘（含中文 Unicode 输入、媒体键）
                                      ├─ 窗口管理（枚举/切换/最小化/关闭）
                                      └─ 应用启动器（apps.json 可自定义）
```

## 功能一览

| 模块 | 内容 |
|---|---|
| 触控板 | 单指拖动移指针 · 轻点=左键 · 双指轻点=右键 · 双指滑动/滚动区=滚轮（灵敏度 1.6 可调） |
| 86 键键盘 | 全键盘布局（字母/数字/符号/F 区缺省）· Shift/Caps/Ctrl/Win/Alt 组合键 · 方向键 · 顶部输入框走系统输入法（支持中文） |
| Dock | 实时窗口列表（切换/最小化/关闭）· 一键打开应用（apps.json 自定义）· 多媒体键（播放/音量/静音）· 快捷键芯片（复制/粘贴/Alt+F4/显示桌面/锁屏…） |
| 连接 | 记住上次 IP 自动重连 · 局域网自动搜索（WebRTC 推断网段 + 并发探测）· UDP 广播发现（端口 8766，供原生 App 用） |
| UI | 现代设计 · 亮/暗主题（跟随系统+手动切换）· 响应式布局 · iPhone 加到主屏幕后全屏 PWA 体验 |
| PC 端 | 托盘常驻 · 开机自启（托盘菜单开关）· 连接信息窗口 · 单文件发布 180MB 免安装 |

## 快速开始

### 电脑端
1. 运行 `PadLink.exe`（托盘出现图标；已设开机自启）
2. 首次联网弹出防火墙提示 → 勾选"专用网络"允许
3. 托盘双击图标打开控制页；右键菜单：打开控制页 / 连接信息 / 开机自启 / 退出

### 手机端
1. 连同一 WiFi，浏览器打开电脑显示的地址（如 `http://192.168.1.100:8765`）
2. iPhone Safari：分享 → **添加到主屏幕** → 从主屏幕打开即为全屏 App

## 自定义应用磁贴

编辑 exe 同目录的 `apps.json`：

```json
{ "apps": [ { "name": "微信", "cmd": "C:\\Program Files\\Tencent\\WeChat\\WeChat.exe" } ] }
```

`cmd` 支持任意 exe 路径、PATH 中的程序名、URI（如 `ms-screenclip:`）。

## 从源码构建

```
cd windows-app
dotnet publish -c Release        # 产物: bin/Release/net8.0-windows/win-x64/publish/
```

依赖：.NET 8 SDK（Windows）。发布为自包含单文件，目标机无需装运行时。

## 故障排查

- 手机连不上：确认同 WiFi（路由器 AP 隔离会拦截）· 托盘程序在运行 · 防火墙放行 8765
- IP 变了：连接页点「自动搜索局域网电脑」，或托盘"连接信息"里看当前 IP
- 中文输入没反应：先用鼠标点一下目标窗口再输入
- 日志：exe 同目录 `padlink.log`

## 安全提示

明文 WebSocket + 完全控制权限，**仅在可信的家庭局域网使用**，勿在公共 WiFi 使用。
