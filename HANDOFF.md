# PadLink 项目交接文档

> 最后更新：2026-11-12 · 由 ZCode 会话 sess_811bc903 生成

---

## 一、项目愿景

把手机变成 Windows 的无线触控板、键盘和遥控器。**脱离物理键盘鼠标，仅凭手机就能完全操控电脑。** 三端共享同一套 WebSocket 协议：网页 PWA（iPhone 主力）、安卓原生 App、Windows 桌面控制台。

---

## 二、需求清单与状态

### ✅ 已实现

#### 触控板
| 功能 | 说明 |
|---|---|
| 单指拖动移动指针 | rAF/Choreographer 帧批量合并，1.9× 提速 |
| 轻点=左键，双指轻点=右键 | 触摸持续时间 < 250ms 且位移 < 10px |
| 双指滑动 = 滚动 | 自然方向，刻度震动反馈 |
| 灵敏度可调 | 0.8~3.0，客户端设置页滑杆 |
| 滚动速度可调 | 1.0~4.0 |
| 亚像素累积 | < 1px 位移不丢弃，累积到下一帧 |
| 背压保护 | `bufferedAmount > 4096` 时合并不丢弃 |
| 触摸时帮助水印淡出 | 闲置 4s 后恢复，可关闭 |

#### 鼠标按键
| 按键 | 实现方式 |
|---|---|
| 左键 | SendInput LEFTDOWN/UP |
| 中键 | MIDDLEDOWN/UP |
| 右键 | RIGHTDOWN/UP |
| 后退(X1) | XDOWN/XUP + mouseData=XBUTTON1 |
| 前进(X2) | XDOWN/XUP + mouseData=XBUTTON2 |
| 拖拽 | 按住=DOWN，松开=UP（pointerdown/up） |
| 亚像素余量 | MouseMove < 1px 累积到 _remX/_remY |

#### 键盘
| 功能 | 说明 |
|---|---|
| 86 键全布局 | 字母/数字/符号/F1-F12/方向键/修饰键 |
| Shift/CapsLock | 异或逻辑（与真实键盘一致） |
| Ctrl/Win/Alt 粘滞 | 点按锁定 → 下一个键发组合 → 自动解锁 |
| 不调起手机系统输入法 | 网页：自建 #kbDisplay 缓冲区（无 `<input>`）；安卓：TextView 替代 EditText |
| 中文输入 | 语音按钮（Web Speech API）→ 识别结果直发电脑（Unicode SendInput） |
| 缓冲整句发送 | 260ms 停顿整段发出，杜绝镜像跳变 |
| 退格 | 缓冲非空删本地字符；空则发 backspace 给电脑 |

#### 媒体控制
播放/暂停/上一曲/下一曲/停止 · 音量加/减/静音（`media_volume_*` VK 码）· **主机音量滑杆**（Core Audio `IAudioEndpointVolume`，读取/设置精确到 1%）

#### 应用与窗口
| 功能 | 服务端实现 |
|---|---|
| 实时窗口列表（含图标/当前徽章/最小化状态） | `EnumWindows` + `IsWindowVisible` + DWMWA_CLOAKED + WS_EX_TOOLWINDOW 过滤 |
| 切换窗口 | `SetForegroundWindow` + ALT 模拟规避前台锁定 |
| 最小化/还原 | `ShowWindowAsync(SW_MINIMIZE/SW_RESTORE)` |
| 关闭窗口 | `PostMessage(WM_CLOSE)` → 700ms 后仍存在则 Alt+F4 兜底 |
| 一键打开程序 | `Process.Start` + 开始菜单快捷方式扫描 + PATH + URI |
| 程序搜索 | 开始菜单 .lnk 文件名匹配（缓存 10 分钟） |

#### 屏幕镜像
| 功能 | 说明 |
|---|---|
| 实时截屏 | `Graphics.CopyFromScreen` → JPEG（质量/宽度可调） |
| 点画面 = 点击定位 | 归一化坐标 → 物理像素（PerMonitorV2 DPI） |
| 自动刷新 | 1.4s 间隔，可开关 |
| 多显示器 | `EnumDisplayMonitors` 列表 + `?monitor=N` 截取指定屏 |

#### 文件管理
浏览（桌面/下载/文档/图片/视频/音乐/磁盘根）· 预览（图片/视频/音频/文本，UTF-8+GBK 自动检测）· 上传（multipart → 目录写入，重名自动编号）· 下载（inline + Content-Disposition）· 删除 · 用电脑打开。安全：拒绝 `C:\Windows`/`Program Files` 等敏感路径。

#### 剪贴板同步
读取（`Clipboard.GetDataObject`，STA 线程 + 3 次重试）· 写入（`Clipboard.SetDataObject(text, true, 5, 120)`）· 网页取/发/粘贴按钮

#### 电源控制
锁定（`LockWorkStation`）· 睡眠（`SetSuspendState`）· 注销（`shutdown /l`）· 重启（`/r /t 3`）· 关机（`/s /t 3`）· 危险操作二次确认

#### 语音输入
Web Speech API `SpeechRecognition`（lang=zh-CN，continuous=true）→ 识别结果直发电脑。Android 17 不支持此 API（需 Chrome）

#### 系统信息
CPU%（PerformanceCounter）· 内存% · 运行时长 · 电量（PowerStatus）· 主机名/OS 版本

#### 局域网发现
| 方式 | 说明 |
|---|---|
| UDP 广播 | 服务端监听 8766 端口，收到 `PADLINK_DISCOVER` 回复 `{t:"padlink",ip,port,name}` |
| WebRTC 网段推断 | 手机端 `RTCPeerConnection` ICE 候选提取子网前缀，并发 HTTP 探测 /24 |
| 记住 IP | localStorage `pl_host`，下次打开自动重连 |

#### 仿生震动
| 模式 | 网页（`navigator.vibrate`） | 安卓（`VibrationEffect`） |
|---|---|---|
| 轻点 | [9] | createOneShot(9, amp) |
| 左键 | [13] | 振幅按强度档缩放 |
| 右键 | [8,26,8] 双段 | createWaveform |
| 中键 | [6,20,6,20,6] 三段 | 同上 |
| 滚轮刻度 | [4] | 5ms 90振幅 |
| 切页 | [10,30,12] | — |
| 强度档 | 轻0.55/标准1/强1.55 | 振幅 120/190/255 |

---

### ⏳ 待实现（用户提出但尚未完成）

| 功能 | 状态 | 说明 |
|---|---|---|
| 手机作主机麦克风（对讲模式） | 服务端 waveOut 已就绪 | 手机端需 AudioRecord 采集 → WS 二进制帧发送 |
| 手机作主机摄像头 | 需虚拟摄像头驱动或帧接收窗口 | 手机端 getUserMedia video → 逐帧发送 |
| 多显示器切换（安卓端） | 服务端已支持 `?monitor=N` | 安卓 UI 选择器待加 |
| 单文件 exe（WinUI 3） | `pack-single.ps1` 已写 | launcher 内嵌 payload.zip → 解压到 LOCALAPPDATA |
| Android 17 真机验证 | 新 APK 已修横屏闪退（移除 sensorLandscape/WindowInsetsController） | 用户反馈横屏仍闪退后加了更多修复，最新 APK 待验证 |
| 语音输入（安卓原生） | Web Speech API 仅限 Chrome | 安卓可用 SpeechRecognizer API |

---

## 三、架构

### 协议（WebSocket ws://<PC-IP>:8765/ws）

#### 文本帧
```
紧凑高频（热路径）：
  M<dx>,<dy>          → 鼠标移动（浮点，亚像素）
  S<dx>,<dy>          → 滚动
  P<ts>               → ping（服务端回 pong）

JSON（低频/复杂）：
  {"t":"click","btn":"left|right|middle|back|forward","count":1}
  {"t":"down"/"up","btn":"..."}
  {"t":"key","value":"ctrl+shift+s","count":1}
  {"t":"text","value":"hello 世界"}
  {"t":"get_volume"} / {"t":"set_volume","level":50} / {"t":"toggle_mute"}
  {"t":"get_clipboard"} / {"t":"set_clipboard","text":"..."}
  {"t":"power","action":"lock|sleep|hibernate|signout|restart|shutdown|cancel"}
  {"t":"list_windows"} → 回 {"t":"windows","list":[{id,title,pid,minimized,foreground}]}
  {"t":"focus_window","id":N} / {"t":"min_window","id":N} / {"t":"close_window","id":N}
  {"t":"list_apps"} → 回 {"t":"apps","list":[{name,cmd}]}
  {"t":"open_app","name":"..."}
  {"t":"launch_program","target":"..."}
  {"t":"get_sysinfo"} → 回 {"t":"sysinfo","info":{cpu,ram,uptime,battery,host,os}}
  {"t":"get_cursor"} → 回 {"t":"cursor","x":0.5,"y":0.5}
  {"t":"screen_move","x":0.5,"y":0.5} / {"t":"screen_click","x":0.5,"y":0.5,"btn":"left"}
  {"t":"search_programs","q":"..."}
  {"t":"get_volume"}
```

#### 二进制帧
```
原始 PCM（16bit/16kHz/mono）→ AudioPlaybackService.WritePcm → waveOut 播放
```

#### HTTP 端点
```
GET  /                          → index.html（嵌入资源）
GET  /static/*                  → 前端资源（lucide.min.js/manifest.json/icon.png）
GET  /screen?w=1280&q=70&monitor=N → JPEG 截屏
GET  /icon?pid=N 或 ?app=名称    → 64×64 PNG 图标
GET  /files?path=...            → 目录列表 JSON
GET  /file?path=...&raw=1       → 文件内容（文本 JSON / 二进制 inline）
POST /upload?dir=...            → multipart 文件上传
```

### Windows 端（WinUI 3 分层架构）
```
windows/src/
├── PadLink.Core/          net8.0 — 无 Windows 依赖
│   ├── PadLinkConstants.cs  端口/路径/设置模型
│   └── Storage.cs           Log / SettingsStore / AppsStore
├── PadLink.Interop/       net8.0-windows — 全部系统调用
│   ├── InputService.cs      SendInput（鼠标/键盘/Unicode/媒体键/X键）
│   ├── WindowService.cs     EnumWindows / 图标 / 焦点 / 关闭
│   ├── ScreenService.cs     CopyFromScreen / 坐标映射 / 剪贴板 / 电源 / 系统信息
│   ├── VolumeService.cs     Core Audio + ProgramLauncher（搜索/启动）
│   ├── FileService.cs       目录浏览/预览/上传/删除
│   ├── AppLauncher.cs       UDP 广播发现 + DiscoverListener
│   ├── WebSocketHandler.cs  WS 消息分发（紧凑协议 + JSON）
│   └── WebHostBuilder.cs    Kestrel 路由（静态文件从嵌入资源读取）
├── PadLink.App.WinUI/     WinUI 3 界面（MVVM）
│   ├── App.xaml(.cs)        单实例 Mutex + EventWaitHandle 唤回
│   ├── MainWindow.xaml(.cs) NavigationView + 自定义标题栏
│   ├── ViewModels/ShellViewModel.cs  ObservableObject 状态
│   ├── Views/OverviewPage   概览（URL+状态+二维码）
│   ├── Views/AppsPage       应用管理
│   ├── Views/ClientsPage    客户端+设置
│   └── Services/            TrayIcon(Shell_NotifyIcon) / StartupRegistration(HKCU Run) / ServerHost
└── PadLink.Launcher/      单文件启动器
    └── Program.cs           解压 payload.zip → 启动 PadLink.UI.exe
```

### 安卓端（Kotlin，程序化 UI）
```
android/app/src/main/java/com/padlink/app/
├── MainActivity.kt          主界面（7 页签 Dock + 触控板 + 键盘 + 媒体 + 应用 + 设置）
└── ExtraPages.kt            屏幕/文件/剪贴板/电源页
```

### 网页端
```
windows/src/PadLink.App.WinUI/wwwroot/
├── index.html               唯一前端文件（CSS+JS 内嵌）
├── manifest.json             PWA 清单（landscape/fullscreen）
├── icon.png                  PWA 图标（Fluent 风格渐变+指针）
└── lucide.min.js             Lucide 图标库（本地化，v0.469.0）
```

---

## 四、关键实现细节

### 低延迟优化
| 层 | 优化 | 效果 |
|---|---|---|
| 客户端 | rAF 合并 touchmove → 每帧最多 1 条 | 400→200 条（1.9×） |
| 协议 | 紧凑 `M<dx>,<dy>` 替代 JSON | 载荷 -67% |
| 服务端 | 紧凑协议同步执行（不 Task.Run） | 消除线程池切换 1-5ms |
| 服务端 | JsonDocument.Parse 直接吃 UTF8 字节 | 免 UTF8→string 分配 |
| 服务端 | Kestrel 默认 NoDelay | RTT p99 0.176ms |
| 输入 | SendInput 亚像素余量累积 | 慢速拖动不丢像素 |
| 输入 | Scroll 亚像素余量累积 | 慢滚不过冲 |

### Windows 桌面 GUI（WinUI 3）
- 自定义标题栏（WindowChrome + SetTitleBar）
- 左侧导航（品牌区 + 主要/配置分组 + 底部状态条）
- 三页：概览（连接地址+二维码）/ 应用管理（DataGrid）/ 设置（开关）
- TrayIcon：手写 Shell_NotifyIcon（HiDPI SM_CXSMICON）
- 开机自启：HKCU Run，值带 `--tray` 参数
- 设置：`%LOCALAPPDATA%\PadLink\settings.json`
- 应用列表：`%LOCALAPPDATA%\PadLink\apps.json`

### 安全限制
- WebSocket 明文协议，仅限可信局域网
- 文件服务拒绝 `C:\Windows` / `Program Files` 等敏感路径
- 无认证（可选加 token 校验）

---

## 五、已知问题与坑

### 环境
| 问题 | 解法 |
|---|---|
| `github.com:443` 被墙 | 用 `api.github.com` Contents API 推送（脚本 `test/push_curl.sh`） |
| Python urllib SSLEOFError | 用 curl `--ssl-no-revoke`（修 CRYPT_E_REVOCATION_OFFLINE） |
| Windows Python 看不到 `/tmp` | 用 `cygpath -w` 转换路径 |
| PowerShell 脚本含中文报错 | 脚本用英文，或加 `-Encoding UTF8` BOM |
| Gradle 下载超时 | 用腾讯镜像 `mirrors.cloud.tencent.com/gradle/` |
| 模拟器 ps16k 镜像不稳定 | 改用 `android-36.1;google_apis;x86_64`（标准 4KB 页） |
| 编译前不 taskkill 会 MSB3027 文件锁定 | 必须 `taskkill //IM PadLink.* //F` 先杀进程 |
| 构建失败但 grep 只显示"个错误"看不到详情 | 必须用 `grep "error CS"` 查看完整错误 |

### 代码
| 坑 | 说明 |
|---|---|
| `JsonElement` 在 Task.Run 里用完后 document 被 dispose | 必须 `.Clone()` 后再传 |
| Kestrel `Map("/ws", b => b.Run(ctx => { ...; return Task.CompletedTask; }))` | HttpContext 被提前 dispose → IFeatureCollection ObjectDisposedException。必须 `Run(async ctx => await handler(ctx))` |
| `ConfigureWebHost` 不注册 IServer | 必须 `ConfigureWebHostDefaults` |
| AGP `ExtractIconExW` P/Invoke | 必须完整 5 参数签名，用 `shell32.dll` 不是 `user32.dll` |
| Kotlin 局部函数不能前向引用 | 用 `lateinit var f: (T) -> Unit` 然后赋值 |
| WPF+WinForms 共存命名冲突 | 文件头用别名 `using Color = System.Windows.Media.Color` 等 |
| `Environment.ProcessPath` 在 launcher 场景指 UI 子进程 | 用 `PADLINK_LAUNCHER` 环境变量传 launcher 真实路径 |
| `zipalign -P 16` 只对齐 .so | dex/arsc 用 `-f 4096` 对齐（4KB 即可） |
| `SdkManager --list` 需要 JAVA_HOME | 必须设 `JAVA_HOME=C:\Program Files\Microsoft\jdk-17.0.21.21-hotspot` |
| `android/local.properties` 路径必须正斜杠 | `sdk.dir=C:/Users/trump/AppData/Local/Android/Sdk` |

---

## 六、文件清单

### 项目根目录
```
padlink/
├── app-wpf/                 旧 WPF 版本（已被 WinUI 替代，保留作参考）
├── app/                     旧 WinForms 版本（同上）
├── android/                 安卓项目
│   ├── app/src/main/java/com/padlink/app/MainActivity.kt
│   ├── app/src/main/java/com/padlink/app/ExtraPages.kt
│   ├── app/src/main/res/    (drawable/ layout/ values/ mipmap-*/)
│   └── build.gradle.kts
├── windows/                 WinUI 3 项目（当前主力）
│   ├── pack-single.ps1      单文件打包
│   ├── Directory.Build.props
│   └── src/
│       ├── PadLink.Core/
│       ├── PadLink.Interop/
│       ├── PadLink.App.WinUI/
│       └── PadLink.Launcher/
├── windows-server/          旧 Python 服务端（备用）
├── test/                    测试脚本
│   ├── kbhook_test.py       键盘钩子全面验证
│   ├── push_curl.sh         GitHub Contents API 推送
│   └── ...
├── dist/                    打包产物（gitignore）
├── .github/workflows/
│   ├── build-apk.yml        安卓 CI
│   └── build-ipa.yml        iOS CI（手动触发）
├── README.md
└── .gitignore
```

### 部署路径
```
C:\Users\trump\PadLink\           ← Windows 运行目录
    PadLink.exe                   ← 165MB 单文件 launcher
    (apps.json / padlink.log)     ← 运行时自动生成，可删
C:\Users\trump\Downloads\sidestore-setup\
    PadLink.apk                   ← 最新安卓 APK
    PadLink-诊断版.apk            ← 896KB 极简版（排障用）
    PadLink-debug-min.apk         ← 二分定位版
```

---

## 七、构建与部署

### Windows（WinUI 3）
```bash
# 开发构建
C:/Users/trump/.dotnet/dotnet.exe build windows/src/PadLink.App.WinUI/PadLink.App.csproj -c Release

# 单文件打包（3-5 分钟）
powershell -NoProfile -ExecutionPolicy Bypass -File windows/pack-single.ps1
# → dist/PadLink.exe
```

### 安卓
```bash
export JAVA_HOME="C:\\Program Files\\Microsoft\\jdk-17.0.21.21-hotspot"
cd android
C:/Users/trump/AppData/Local/Temp/gradle-8.11.1/bin/gradle.bat assembleDebug --no-daemon
# → app/build/outputs/apk/debug/app-debug.apk

# 或触发 CI（github.com 被墙时用 Contents API）
curl -X POST -H "Authorization: token $TOKEN" \
  "https://api.github.com/repos/Pusooy/padlink/actions/workflows/build-apk.yml/dispatches" \
  -d '{"ref":"main"}'
```

### GitHub 推送（github.com 被墙时）
```bash
cd test && bash push_curl.sh
# 通过 api.github.com Contents API 逐文件推送
# 首次比对远端 tree SHA，只推变化的文件
```

---

## 八、协议变更记录

| 版本 | 变更 |
|---|---|
| v1.0 | JSON 协议（move/click/scroll/text/key/power/list_*/focus/min/close/open_app） |
| v1.1 | + get_clipboard / set_clipboard / get_sysinfo / get_cursor / screen_move / screen_click |
| v1.2 | + 紧凑协议 M/S（鼠标移动/滚动），sync Dispatch（消除 Task.Run） |
| v1.3 | + list_monitors / set_monitor（多显示器）/ search_programs / audio_start/stop/data |

---

## 九、下一个开发者需要知道的

1. **Android 17 闪退仍未 100% 确认根因**——已排除 AppCompat、targetSdk、orientation、WindowInsetsController 等因素。最新 APK 加了闪退兜底（屏幕显示堆栈）。需要真机 logcat 或装诊断版 APK 二分定位。
2. **模拟器 ps16k 镜像在这台机器不稳定**（surfaceflinger 崩溃、package 服务跳变）。改用 `android-36.1;google_apis;x86_64` 标准镜像更稳（6 秒启动，服务稳定）。
3. **网页触控板延迟已优化到极限**（rAF 合并 + 紧凑协议 + 亚像素 + 同步 SendInput）。如需进一步优化，考虑改用 PointerEvent + `getCoalescedEvents()` 或 WebSocket 二进制帧。
4. **`push_curl.sh` 是 GitHub 推送的唯一可靠方式**（github.com:443 被墙）。每次 API PUT 会触发一次 CI 运行，所以 build-ipa.yml 已改为手动触发。
5. **apps.json 兼容**：新版从 `%LOCALAPPDATA%\PadLink\apps.json` 读取，旧版从 exe 同目录读取。FileService 的 ProgramLauncher 会自动扫描开始菜单，apps.json 是补充。
