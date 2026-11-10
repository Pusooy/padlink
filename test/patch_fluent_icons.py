# -*- coding: utf-8 -*-
"""
用 Segoe Fluent Icons（Windows 11 系统自带字体）替换文字符号图标。
这是微软官方 Fluent Design 图标字体，与 Win11 设置/资源管理器完全一致。
字形码点参考：https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\UiKit.cs"
s = io.open(P, encoding="utf-8").read()

# 在 UiKit 末尾追加 FluentIcons 帮助类
if "class FluentIcons" not in s:
    s = s.rstrip() + r'''

/// <summary>
/// Fluent 图标：使用 Windows 11 自带的 Segoe Fluent Icons 字体。
/// 相比文字符号（◉ ▦ ⚙）是真正的矢量图标，与系统设置界面一致。
/// </summary>
public static class FluentIcons
{
    /// <summary>字体名；Win10 回退到 Segoe MDL2 Assets。</summary>
    public static readonly FontFamily Family = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // ---- 常用字形（Win11 / Win10 MDL2 通用码点）----
    public const string Home = "\uE80F";            // 主页
    public const string Settings = "\uE713";        // 齿轮
    public const string Apps = "\uE71D";            // 应用网格
    public const string MousePointer = "\uE962";    // 指针
    public const string Keyboard = "\uE765";        // 键盘
    public const string Play = "\uE768";            // 播放
    public const string Screen = "\uE7F4";          // 显示器
    public const string Folder = "\uE8B7";          // 文件夹
    public const string Copy = "\uE8C8";            // 剪贴板
    public const string Power = "\uE7E8";           // 电源
    public const string Link = "\uE71B";            // 链接
    public const string Refresh = "\uE72C";         // 刷新
    public const string Info = "\uE946";            // 信息
    public const string ChevronRight = "\uE76C";    // 右箭头
    public const string ChevronLeft = "\uE76B";
    public const string Check = "\uE73E";           // 对勾
    public const string Copy2 = "\uE8C8";
    public const string QRCode = "\uED14";          // 二维码
    public const string FolderOpen = "\uE838";      // 打开文件夹
    public const string Document = "\uE8A5";        // 文档
    public const string Log = "\uE9D9";             // 日志/条目
    public const string Exit = "\uE8BB";            // 退出
    public const string Rocket = "\uE82D";          // 启动/发射

    /// <summary>创建一个 Fluent 图标 TextBlock。</summary>
    public static TextBlock Icon(string glyph, double size = 16, Brush? color = null) => new()
    {
        Text = glyph,
        FontFamily = Family,
        FontSize = size,
        Foreground = color ?? Tokens.Ink2,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };
}
'''
io.open(P, "w", encoding="utf-8").write(s)
print("FluentIcons 帮助类已添加")

# ---- MainWindow: 用 Fluent 图标替换文字符号 ----
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\MainWindow.cs"
m = io.open(P2, encoding="utf-8").read()

# 1) 标题栏 Logo：用 Fluent 的指针图标代替 "◉"
m = m.replace('''            Child = new TextBlock
            {
                Text = "◉", Foreground = Brushes.White, FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },''',
'''            Child = new TextBlock
            {
                Text = FluentIcons.MousePointer, Foreground = Brushes.White,
                FontFamily = FluentIcons.Family, FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },''')

# 2) 导航项调用改为 Fluent 字形
m = m.replace('AddNavItem("home", "◉", "概览");\n        AddNavItem("apps", "▦", "应用管理");\n        AddNavItem("settings", "⚙", "设置");',
'''AddNavItem("home", FluentIcons.Home, "概览");
        AddNavItem("apps", FluentIcons.Apps, "应用管理");
        AddNavItem("settings", FluentIcons.Settings, "设置");''')

# 3) 导航图标 TextBlock 使用 Fluent 字体
m = m.replace('''        var g = new TextBlock
        {
            Text = glyph, FontSize = 14, Width = 24,
            Foreground = Tokens.Ink2, VerticalAlignment = VerticalAlignment.Center,
        };''',
'''        var g = new TextBlock
        {
            Text = glyph, FontSize = 15, Width = 26,
            FontFamily = FluentIcons.Family,
            Foreground = Tokens.Ink2, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
        };''')

# 4) 能力列表图标换成 Fluent 字形
m = m.replace('''        foreach (var (icon, title, desc) in new[]
        {
            ("◉", "触控板 + 手势", "单指移动 / 轻点左键 / 双指右键 / 滑动滚动"),
            ("⌨", "86 键键盘 + 语音", "完整键盘布局，支持语音转文字直输"),
            ("▶", "媒体与音量", "播放控制、主机音量滑杆、系统快捷键"),
            ("▦", "应用与窗口", "实时窗口切换/关闭，程序搜索启动"),
            ("▣", "屏幕镜像", "查看电脑画面，点按即定位点击"),
            ("▤", "文件管理", "浏览/预览/上传/下载电脑文件"),
            ("⎘", "剪贴板同步", "手机与电脑剪贴板双向互通"),
            ("⏻", "电源控制", "锁定、睡眠、注销、重启、关机"),
        })''',
'''        foreach (var (icon, title, desc) in new[]
        {
            (FluentIcons.MousePointer, "触控板 + 手势", "单指移动 / 轻点左键 / 双指右键 / 滑动滚动"),
            (FluentIcons.Keyboard, "86 键键盘 + 语音", "完整键盘布局，支持语音转文字直输"),
            (FluentIcons.Play, "媒体与音量", "播放控制、主机音量滑杆、系统快捷键"),
            (FluentIcons.Apps, "应用与窗口", "实时窗口切换/关闭，程序搜索启动"),
            (FluentIcons.Screen, "屏幕镜像", "查看电脑画面，点按即定位点击"),
            (FluentIcons.Folder, "文件管理", "浏览/预览/上传/下载电脑文件"),
            (FluentIcons.Copy, "剪贴板同步", "手机与电脑剪贴板双向互通"),
            (FluentIcons.Power, "电源控制", "锁定、睡眠、注销、重启、关机"),
        })''')

m = m.replace('''            head.Children.Add(new TextBlock { Text = icon, FontSize = 13, Foreground = Tokens.AccentBrush, Margin = new Thickness(0, 0, 7, 0) });''',
'''            head.Children.Add(new TextBlock
            {
                Text = icon, FontSize = 14, FontFamily = FluentIcons.Family,
                Foreground = Tokens.AccentBrush, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });''')

# 5) 按钮加图标
m = m.replace('''        var open = Buttons.Primary("打开控制页", (s, e) =>''',
'''        var open = Buttons.PrimaryWithIcon(FluentIcons.Link, "打开控制页", (s, e) =>''')
m = m.replace('''        var copy = Buttons.Ghost("复制地址", (s, e) =>''',
'''        var copy = Buttons.GhostWithIcon(FluentIcons.Copy, "复制地址", (s, e) =>''')
m = m.replace('''        var qr = Buttons.Ghost("显示二维码", (s, e) => ShowQrWindow());''',
'''        var qr = Buttons.GhostWithIcon(FluentIcons.QRCode, "显示二维码", (s, e) => ShowQrWindow());''')

io.open(P2, "w", encoding="utf-8").write(m)
print("MainWindow: 已替换为 Fluent 图标")
