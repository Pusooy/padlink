# PadLink —— iPhone 无线触控板 + 键盘（含真实震动反馈）

用 SideStore 长期免费安装的自研 iOS App，通过 WiFi 局域网控制 Windows 鼠标键盘。
震动走系统级 `UIImpactFeedbackGenerator`，这是 PWA 做不到的。

```
iPhone PadLink App ──WebSocket(8765端口)──> Windows server.py ──> 控制鼠标/键盘
```

## 目录结构

```
padlink/
├── windows-server/          ← 电脑端（已在本机测试通过）
│   ├── server.py            服务端主程序
│   └── 启动服务端.bat        双击启动
├── ios/                     ← iPhone 端（SwiftUI 源码）
│   ├── project.yml          XcodeGen 工程定义
│   └── PadLink/             Swift 源码
└── .github/workflows/build-ipa.yml   ← GitHub 云端编译 IPA（无需 Mac）
```

## 你需要动手的步骤（按顺序）

### A. 准备电脑（5 分钟）

1. 双击安装 `C:\Users\trump\Downloads\sidestore-setup\iloader-windows-x64.msi`
   （iTunes 已由助手装好；如识别不了手机再从 Microsoft Store 装「Apple Devices」）
2. 服务端不用动，`padlink\windows-server\` 已经就绪

### B. 云端编译 App（10 分钟，一次性）

3. 打开 github.com 登录 → 右上角 + → New repository
   - 名称随意（如 `padlink`），**选 Public**（免费白嫖 macOS 编译机），Create
4. 在新仓库页面点 **uploading an existing file**（或 Add file → Upload files）
5. 把 `padlink` 文件夹里的**全部内容**（`.github`、`ios`、`windows-server`、`README.md`）拖进网页 → Commit changes
6. 仓库顶部 **Actions** 标签 → 等 "Build IPA" 变绿（约 3~5 分钟）
   - 如果红了：点进去看报错截图发给我
7. 进该次运行 → 页面底部 **Artifacts** → 下载 `PadLink-ipa`
8. 解压 zip 得到 `PadLink.ipa`，通过微信/QQ/数据线/网盘传到 iPhone（存到「文件」App 里）

### C. 安装 SideStore（15 分钟，一次性）

9. iPhone App Store 安装 **LocalDevVPN**，打开点 Connect，允许 VPN 配置
10. 数据线连接 iPhone ↔ Windows，手机点「信任」并输锁屏密码
11. 打开 iloader → 登录 Apple ID → 选中你的 iPhone → **Install SideStore (Stable)**
12. 手机：设置 → 通用 → VPN与设备管理 → 信任你的 Apple ID 证书
13. 手机：设置 → 隐私与安全性 → 开发者模式 → 开启（自动重启）
14. 打开 LocalDevVPN 点 Connect → 打开 SideStore → 登录同一 Apple ID
15. My Apps 页 → 点 SideStore 右侧「7 DAYS」刷新一次 → 弹窗问证书时点 Yes/Refresh Now

### D. 装 PadLink 并连接（2 分钟）

16. SideStore → 右上角 **+** → 从「文件」里选中 `PadLink.ipa` → 安装
17. 电脑双击 `padlink\windows-server\启动服务端.bat`，**防火墙弹窗勾选专用网络并允许**
18. iPhone 打开 PadLink → 填入服务端窗口显示的 IP → 连接 → 开用！

## 日常使用

- 电脑：双击 `启动服务端.bat`
- 手机：打开 PadLink 连接即可
- 续签：**无需电脑**，LocalDevVPN 保持开启、手机连着 WiFi 就会自动续签
- 免费账号限制：最多 3 个自装 App（SideStore 占 1 个），PadLink 占 1 个

## 触控板操作说明

| 手势 | 效果（带震动反馈） |
|---|---|
| 单指拖动 | 移动鼠标指针 |
| 轻点 | 左键单击 |
| 顶部鼠标图标 | 右键单击 |
| 下方滚动条上下拖 | 滚轮（滚动时有细腻震动刻度） |
| 顶部键盘图标 | 打开文字输入，实时打到电脑（支持中文） |
| 回车/退格按钮 | 对应按键 |

## 故障排查

- **连不上**：确认同一 WiFi（有些路由器开了 AP 隔离会不通）；IP 变了就重新看服务端窗口
- **防火墙**：Windows 设置 → 防火墙 → 允许应用 → 勾选 Python（专用）
- **SideStore 闪退/装不上**：先确认 LocalDevVPN 已 Connect、开发者模式已开
- **证书过期 App 打不开**：SideStore → My Apps → 点该 App 的倒计时手动刷新

## 安全提示

协议是明文 WebSocket，仅在**可信的家庭局域网**内使用，勿在公共 WiFi 使用。
