using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using Image = System.Windows.Controls.Image;
using Path = System.IO.Path;
using File = System.IO.File;
using Control = System.Windows.Controls.Control;
using ControlTemplate = System.Windows.Controls.ControlTemplate;
using FrameworkElementFactory = System.Windows.FrameworkElementFactory;
using TemplateBindingExtension = System.Windows.TemplateBindingExtension;

namespace PadLink.Wpf;

/// <summary>PadLink 桌面控制台（WPF，自绘无边框窗口）。</summary>
public sealed class MainWindow : Window
{
    private readonly Grid _root = new();
    private readonly ContentControl _pageHost = new();
    private readonly StackPanel _navPanel = new();
    private readonly List<(object Item, string Key, TextBlock Label, TextBlock Glyph)> _navItems = new();
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new();

    private TextBlock _statusText = new();
    private Ellipse _statusDot = new();
    private TextBlock _urlText = new();
    private TextBlock _ipText = new();
    private DataGrid? _appsGrid;
    private string _activePage = "home";

    public MainWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.CanResize;
        Background = Tokens.Bg;
        Width = 960;
        Height = 640;
        MinWidth = 780;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "PadLink";
        var chrome = new WindowChrome
        {
            CaptionHeight = 38,
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
        };
        WindowChrome.SetWindowChrome(this, chrome);

        ApplyTheme();
        Build();
        ShowPage("home");

        _statusTimer.Interval = TimeSpan.FromSeconds(1.2);
        _statusTimer.Tick += (s, e) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
    }

    private void ApplyTheme()
    {
        bool dark = !IsSystemLightTheme();
        Tokens.SetTheme(dark);
        Background = Tokens.Bg;
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 1;
        }
        catch { return false; }
    }

    // ================= 布局 =================
    private void Build()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧导航
        var nav = new Border
        {
            Background = Tokens.NavBg,
            BorderBrush = Tokens.Edge,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        nav.Child = _navPanel;
        _navPanel.Margin = new Thickness(12, 16, 12, 16);

        // 标题（兼作拖拽区）
        var titleRow = new Grid { Height = 40, Margin = new Thickness(2, 0, 0, 8) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(7),
            Background = Tokens.AccentBrush, Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = "◉", Foreground = Brushes.White, FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        brand.Children.Add(new TextBlock
        {
            Text = "PadLink", FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = Tokens.Ink, VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(brand);

        var winBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        winBtns.Children.Add(CaptionButton("—", () => WindowState = WindowState.Minimized));
        winBtns.Children.Add(CaptionButton("▢", () =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        winBtns.Children.Add(CaptionButton("✕", Close, isClose: true));
        Grid.SetColumn(winBtns, 2);
        titleRow.Children.Add(winBtns);
        _navPanel.Children.Add(titleRow);

        AddNavItem("home", "◉", "概览");
        AddNavItem("apps", "▦", "应用管理");
        AddNavItem("settings", "⚙", "设置");

        // 右侧内容
        var contentWrap = new Grid();
        var pageScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(28, 24, 28, 24),
        };
        pageScroll.Content = _pageHost;
        contentWrap.Children.Add(pageScroll);

        _pages["home"] = BuildHomePage();
        _pages["apps"] = BuildAppsPage();
        _pages["settings"] = BuildSettingsPage();

        Grid.SetColumn(nav, 0);
        Grid.SetColumn(contentWrap, 1);
        root.Children.Add(nav);
        root.Children.Add(contentWrap);
        Content = root;
    }

    private Button CaptionButton(string glyph, Action action, bool isClose = false)
    {
        var b = new Button
        {
            Content = glyph,
            Width = 40, Height = 30,
            FontSize = 12,
            Foreground = Tokens.Ink2,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.MouseEnter += (s, e) => b.Background = isClose
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)) : Tokens.CardAlt;
        b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
        b.Click += (s, e) => action();
        return b;
    }

    private void AddNavItem(string key, string glyph, string label)
    {
        var btn = new Button
        {
            Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 2, 0, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedButtonTemplate(),
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        var g = new TextBlock
        {
            Text = glyph, FontSize = 14, Width = 24,
            Foreground = Tokens.Ink2, VerticalAlignment = VerticalAlignment.Center,
        };
        var l = new TextBlock
        {
            Text = label, FontSize = 13, Foreground = Tokens.Ink2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(g);
        stack.Children.Add(l);
        btn.Content = stack;
        btn.Click += (s, e) => ShowPage(key);
        _navPanel.Children.Add(btn);
        _navItems.Add((null!, key, l, g));
    }

    private static ControlTemplate RoundedButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        return t;
    }

    private void ShowPage(string key)
    {
        _activePage = key;
        _pageHost.Content = _pages[key];
        for (int i = 0; i < _navItems.Count; i++)
        {
            var (_, k, label, glyph) = _navItems[i];
            bool on = k == key;
            if (_navPanel.Children[i + 1] is Button b)
                b.Background = on ? Tokens.AccentSoft : Brushes.Transparent;
            label.Foreground = on ? Tokens.AccentBrush : Tokens.Ink2;
            glyph.Foreground = on ? Tokens.AccentBrush : Tokens.Ink2;
        }
        if (key == "apps") LoadAppsIntoGrid();
    }

    // ================= 概览页 =================
    private FrameworkElement BuildHomePage()
    {
        var stack = new StackPanel();

        stack.Children.Add(Text.H1("概览"));
        stack.Children.Add(Text.Sub("手机与电脑处于同一 WiFi，浏览器或 App 连接下方地址即可操控。"));

        // 连接卡片
        var card = new Card();
        var inner = new StackPanel();
        inner.Children.Add(Text.Label("连接地址"));

        _urlText = new TextBlock
        {
            Text = TrayContext.BaseUrl,
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            Foreground = Tokens.AccentBrush,
            Margin = new Thickness(0, 0, 0, 8),
        };
        inner.Children.Add(_urlText);

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        _statusDot = new Ellipse { Width = 8, Height = 8, Fill = Tokens.Ink3, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        _statusText = new TextBlock { FontSize = 12, Foreground = Tokens.Ink3, VerticalAlignment = VerticalAlignment.Center };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_statusText);
        inner.Children.Add(statusRow);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var open = Buttons.Primary("打开控制页", (s, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TrayContext.BaseUrl) { UseShellExecute = true }); }
            catch { }
        });
        open.Margin = new Thickness(0, 0, 10, 0);
        var copy = Buttons.Ghost("复制地址", (s, e) =>
        {
            try { Clipboard.SetText(TrayContext.BaseUrl); _ipText.Text = "已复制到剪贴板 ✓"; } catch { }
        });
        var qr = Buttons.Ghost("显示二维码", (s, e) => ShowQrWindow());
        qr.Margin = new Thickness(10, 0, 0, 0);
        btns.Children.Add(open);
        btns.Children.Add(copy);
        btns.Children.Add(qr);
        inner.Children.Add(btns);

        _ipText = new TextBlock
        {
            Text = $"局域网 IP {TrayContext.LocalIP}",
            FontSize = 11, Foreground = Tokens.Ink3, Margin = new Thickness(0, 12, 0, 0),
        };
        inner.Children.Add(_ipText);

        card.Child = inner;
        stack.Children.Add(card);

        // 能力卡片
        var feat = new Card();
        var f = new StackPanel();
        f.Children.Add(Text.Label("手机端可用能力"));
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (icon, title, desc) in new[]
        {
            ("◉", "触控板 + 手势", "单指移动 / 轻点左键 / 双指右键 / 滑动滚动"),
            ("⌨", "86 键键盘 + 语音", "完整键盘布局，支持语音转文字直输"),
            ("▶", "媒体与音量", "播放控制、主机音量滑杆、系统快捷键"),
            ("▦", "应用与窗口", "实时窗口切换/关闭，程序搜索启动"),
            ("▣", "屏幕镜像", "查看电脑画面，点按即定位点击"),
            ("▤", "文件管理", "浏览/预览/上传/下载电脑文件"),
            ("⎘", "剪贴板同步", "手机与电脑剪贴板双向互通"),
            ("⏻", "电源控制", "锁定、睡眠、注销、重启、关机"),
        })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 6, 16, 6) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = icon, FontSize = 13, Foreground = Tokens.AccentBrush, Margin = new Thickness(0, 0, 7, 0) });
            head.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Tokens.Ink });
            cell.Children.Add(head);
            cell.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 11, Foreground = Tokens.Ink3,
                Margin = new Thickness(20, 2, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
            grid.Children.Add(cell);
        }
        f.Children.Add(grid);
        feat.Child = f;
        stack.Children.Add(feat);

        return stack;
    }

    private void ShowQrWindow()
    {
        var win = new QrWindow(TrayContext.BaseUrl) { Owner = this };
        win.ShowDialog();
    }

    private void RefreshStatus()
    {
        if (_statusText == null) return;
        int n = WebSocketHandler.ConnectedCount;
        _statusText.Text = n > 0 ? $"已连接 {n} 台设备" : "等待手机连接";
        _statusDot.Fill = n > 0 ? Tokens.OkBrush : Tokens.Ink3;
        var url = TrayContext.BaseUrl;
        if (_urlText.Text != url) _urlText.Text = url;
    }

    // ================= 应用管理页 =================
    private FrameworkElement BuildAppsPage()
    {
        var stack = new StackPanel();
        stack.Children.Add(Text.H1("应用管理"));
        stack.Children.Add(Text.Sub("手机端「应用」页会自动列出开始菜单中的所有程序；这里可补充自定义启动项。"));

        var card = new Card();
        var inner = new StackPanel();

        _appsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Tokens.CardBg,
            Foreground = Tokens.Ink,
            RowBackground = Tokens.CardBg,
            AlternatingRowBackground = Tokens.CardAlt,
            BorderBrush = Tokens.Edge,
            BorderThickness = new Thickness(1),
            HorizontalGridLinesBrush = Tokens.Edge,
            RowHeight = 32,
            MinHeight = 240,
            FontSize = 12.5,
        };
        _appsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "名称（手机显示）",
            Binding = new System.Windows.Data.Binding("Name"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _appsGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "启动命令 / 路径 / URI",
            Binding = new System.Windows.Data.Binding("Cmd"),
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
        });
        inner.Children.Add(_appsGrid);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var save = Buttons.Primary("保存", (s, e) => SaveApps());
        save.Margin = new Thickness(0, 0, 10, 0);
        var add = Buttons.Ghost("添加一行", (s, e) => _appsGrid?.Items.Add(new AppRow("", "")));
        add.Margin = new Thickness(0, 0, 10, 0);
        var reload = Buttons.Ghost("重新载入", (s, e) => LoadAppsIntoGrid());
        btns.Children.Add(save);
        btns.Children.Add(add);
        btns.Children.Add(reload);
        inner.Children.Add(btns);

        card.Child = inner;
        stack.Children.Add(card);
        return stack;
    }

    public sealed class AppRow
    {
        public string Name { get; set; } = "";
        public string Cmd { get; set; } = "";
        public AppRow() { }
        public AppRow(string n, string c) { Name = n; Cmd = c; }
    }

    private void LoadAppsIntoGrid()
    {
        if (_appsGrid == null) return;
        _appsGrid.Items.Clear();
        try
        {
            foreach (dynamic a in AppLauncher.ListAppsRaw())
                _appsGrid.Items.Add(new AppRow((string)a.name, (string)a.cmd));
        }
        catch { }
    }

    private void SaveApps()
    {
        if (_appsGrid == null) return;
        try
        {
            var list = new List<(string, string)>();
            foreach (var item in _appsGrid.Items)
            {
                if (item is not AppRow row) continue;
                var n = (row.Name ?? "").Trim();
                var c = (row.Cmd ?? "").Trim();
                if (n.Length > 0 && c.Length > 0) list.Add((n, c));
            }
            AppLauncher.SaveApps(list);
            MessageBox.Show(this, "已保存。手机端打开「应用」页刷新即可。", "PadLink",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "PadLink",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================= 设置页 =================
    private FrameworkElement BuildSettingsPage()
    {
        var stack = new StackPanel();
        stack.Children.Add(Text.H1("设置"));
        stack.Children.Add(Text.Sub("所有选项保存在当前用户下，无需管理员权限。"));

        // 启动
        var card1 = new Card();
        var s1 = new StackPanel();
        s1.Children.Add(Text.Label("启动"));
        var autoStart = new ToggleSwitch(Autostart.IsEnabled());
        autoStart.Toggled += (s, on) =>
        {
            if (Autostart.IsEnabled() != on) Autostart.Toggle();
        };
        s1.Children.Add(new SettingRow("开机自动启动", "登录 Windows 后自动运行 PadLink", autoStart));

        var autoPage = new ToggleSwitch(AppSettings.AutoOpenPage);
        autoPage.Toggled += (s, on) => AppSettings.AutoOpenPage = on;
        s1.Children.Add(new SettingRow("启动时打开控制页", "默认关闭，避免弹窗打扰", autoPage));

        var trayBalloon = new ToggleSwitch(AppSettings.TrayBalloon);
        trayBalloon.Toggled += (s, on) => AppSettings.TrayBalloon = on;
        s1.Children.Add(new SettingRow("启动气泡提示", "启动后在托盘弹出连接地址", trayBalloon));
        card1.Child = s1;
        stack.Children.Add(card1);

        // 服务
        var card2 = new Card();
        var s2 = new StackPanel();
        s2.Children.Add(Text.Label("服务"));
        var portRow = new StackPanel { Orientation = Orientation.Horizontal };
        portRow.Children.Add(new TextBlock
        {
            Text = $"{TrayContext.Port}", FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5, Foreground = Tokens.Ink,
        });
        s2.Children.Add(new SettingRow("监听端口", "防火墙需放行该端口（安装时已自动添加规则）", portRow));

        var conBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var openDir = Buttons.Ghost("打开程序目录", (s, e) =>
            System.Diagnostics.Process.Start("explorer.exe", AppContext.BaseDirectory));
        openDir.Margin = new Thickness(0, 0, 10, 0);
        var logs = Buttons.Ghost("查看运行日志", (s, e) => ShowLogWindow());
        logs.Margin = new Thickness(0, 0, 10, 0);
        var exit = Buttons.Ghost("退出 PadLink", (s, e) =>
        {
            if (MessageBox.Show(this, "退出后手机将无法连接，确定？", "PadLink",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Application.Current.Shutdown();
        }, Tokens.DangerBrush);
        conBtnRow.Children.Add(openDir);
        conBtnRow.Children.Add(logs);
        conBtnRow.Children.Add(exit);
        s2.Children.Add(conBtnRow);
        card2.Child = s2;
        stack.Children.Add(card2);

        // 关于
        var card3 = new Card();
        var s3 = new StackPanel();
        s3.Children.Add(Text.Label("关于"));
        s3.Children.Add(new TextBlock
        {
            Text = "PadLink 1.3  ·  .NET 8 / WPF\n" +
                   $"服务端口 {TrayContext.Port}  ·  WebSocket 明文协议，仅限可信局域网\n" +
                   "手机端：浏览器 PWA + 原生安卓 App",
            FontSize = 11.5, Foreground = Tokens.Ink3, LineHeight = 19,
        });
        card3.Child = s3;
        stack.Children.Add(card3);

        return stack;
    }

    private void ShowLogWindow()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "padlink.log");
        if (!File.Exists(path)) { MessageBox.Show(this, "暂无日志文件"); return; }
        var win = new Window
        {
            Title = "运行日志",
            Width = 720, Height = 520,
            Background = Tokens.Bg,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var sv = new ScrollViewer { Padding = new Thickness(16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        sv.Content = new TextBlock
        {
            Text = string.Join("\n", File.ReadAllLines(path).TakeLast(400)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11.5,
            Foreground = Tokens.Ink2,
            TextWrapping = TextWrapping.Wrap,
        };
        win.Content = sv;
        win.ShowDialog();
    }
}

/// <summary>二维码弹窗（纯自绘 QR，无需第三方库）。</summary>
public sealed class QrWindow : Window
{
    public QrWindow(string url)
    {
        Title = "扫码连接";
        Width = 380;
        Height = 460;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = "用手机相机扫描", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });
        var img = new Image { Width = 280, Height = 280, Stretch = Stretch.Uniform };
        try { img.Source = QrCode.Render(url, 8); }
        catch (Exception ex)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "二维码生成失败：" + ex.Message, Foreground = Brushes.DarkRed,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        stack.Children.Add(img);
        stack.Children.Add(new TextBlock
        {
            Text = url, FontSize = 13, Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        });
        Content = stack;
    }
}

/// <summary>极简 QR 编码器（字节模式，版本 1-10，纠错等级 M）。</summary>
public static class QrCode
{
    private static readonly int[] EcCodewordsPerBlockM = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
    private static readonly int[] NumBlocksM = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

    public static BitmapSource Render(string text, int moduleSize = 8)
    {
        var matrix = Encode(text);
        int n = matrix.GetLength(0);
        int quiet = 4;
        int total = (n + quiet * 2) * moduleSize;
        var pixels = new byte[total * total];
        for (int y = 0; y < total; y++)
            for (int x = 0; x < total; x++) pixels[y * total + x] = 255;

        for (int my = 0; my < n; my++)
            for (int mx = 0; mx < n; mx++)
            {
                if (!matrix[my, mx]) continue;
                for (int dy = 0; dy < moduleSize; dy++)
                    for (int dx = 0; dx < moduleSize; dx++)
                    {
                        int py = (my + quiet) * moduleSize + dy;
                        int px = (mx + quiet) * moduleSize + dx;
                        pixels[py * total + px] = 0;
                    }
            }

        var bmp = BitmapSource.Create(total, total, 96, 96,
            PixelFormats.Gray8, null, pixels, total);
        bmp.Freeze();
        return bmp;
    }

    // 简化实现：仅支持 version 1-10，字节模式，纠错等级 M
    public static bool[,] Encode(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        int version = 1;
        int capacityBits = 0;
        for (int v = 1; v <= 10; v++)
        {
            int dataCodewords = TotalDataCodewordsM(v);
            capacityBits = dataCodewords * 8 - 4 - (v <= 9 ? 8 : 16);
            if (bytes.Length * 8 <= capacityBits) { version = v; break; }
            if (v == 10) throw new InvalidOperationException("内容过长，无法生成二维码");
        }

        int totalData = TotalDataCodewordsM(version);
        var bits = new List<bool>();
        void Push(int value, int len)
        {
            for (int i = len - 1; i >= 0; i--) bits.Add(((value >> i) & 1) == 1);
        }
        Push(0b0100, 4);                        // 字节模式
        Push(bytes.Length, version <= 9 ? 8 : 16); // 字符计数
        foreach (var b in bytes) Push(b, 8);
        int totalBits = totalData * 8;
        Push(0, Math.Min(4, totalBits - bits.Count)); // 终止符
        while (bits.Count % 8 != 0) bits.Add(false);
        var data = new byte[totalData];
        for (int i = 0; i < bits.Count / 8; i++)
        {
            int v = 0;
            for (int j = 0; j < 8; j++) v = (v << 1) | (bits[i * 8 + j] ? 1 : 0);
            data[i] = (byte)v;
        }
        // 填充
        byte[] pads = { 0xEC, 0x11 };
        int padIdx = 0;
        for (int i = bits.Count / 8; i < totalData; i++) data[i] = pads[padIdx++ % 2];

        int numBlocks = NumBlocksM[version];
        int ecPerBlock = EcCodewordsPerBlockM[version];
        int dataBlocks = numBlocks;
        int dataPerBlock = totalData / dataBlocks;
        var ecBytes = new byte[numBlocks * ecPerBlock];
        var blockData = new byte[dataBlocks][];
        for (int i = 0; i < dataBlocks; i++)
        {
            blockData[i] = new byte[dataPerBlock];
            Array.Copy(data, i * dataPerBlock, blockData[i], 0, dataPerBlock);
            var ec = ReedSolomon(blockData[i], ecPerBlock);
            Array.Copy(ec, 0, ecBytes, i * ecPerBlock, ecPerBlock);
        }

        // 交织
        var final = new List<byte>();
        for (int i = 0; i < dataPerBlock; i++)
            for (int b = 0; b < dataBlocks; b++) final.Add(blockData[b][i]);
        for (int i = 0; i < ecPerBlock; i++)
            for (int b = 0; b < dataBlocks; b++) final.Add(ecBytes[b * ecPerBlock + i]);

        int size = 17 + version * 4;
        var matrix = new bool[size, size];
        var reserved = new bool[size, size];
        PlaceFinders(matrix, reserved, size);
        PlaceTiming(matrix, reserved, size);
        PlaceAlignment(matrix, reserved, size, version);
        PlaceFormatReserve(reserved, size);
        if (version >= 7) PlaceVersionReserve(reserved, size);

        var dataBits = new List<bool>();
        foreach (var b in final) for (int i = 7; i >= 0; i--) dataBits.Add(((b >> i) & 1) == 1);
        PlaceData(matrix, reserved, size, dataBits);
        PlaceFormat(matrix, size, 0); // 纠错等级 M + 掩码 0
        ApplyMask(matrix, reserved, size, 0);
        return matrix;
    }

    private static int TotalDataCodewordsM(int v) => v switch
    {
        1 => 16, 2 => 28, 3 => 44, 4 => 64, 5 => 86,
        6 => 108, 7 => 124, 8 => 154, 9 => 182, 10 => 216, _ => 16,
    };

    private static void PlaceFinders(bool[,] m, bool[,] r, int n)
    {
        void Finder(int ox, int oy)
        {
            for (int y = -1; y <= 7; y++)
                for (int x = -1; x <= 7; x++)
                {
                    int px = ox + x, py = oy + y;
                    if (px < 0 || py < 0 || px >= n || py >= n) continue;
                    bool on = (x >= 0 && x <= 6 && (y == 0 || y == 6)) ||
                              (y >= 0 && y <= 6 && (x == 0 || x == 6)) ||
                              (x >= 2 && x <= 4 && y >= 2 && y <= 4);
                    m[py, px] = on;
                    r[py, px] = true;
                }
        }
        Finder(0, 0);
        Finder(n - 7, 0);
        Finder(0, n - 7);
    }

    private static void PlaceTiming(bool[,] m, bool[,] r, int n)
    {
        for (int i = 8; i < n - 8; i++)
        {
            m[6, i] = i % 2 == 0; r[6, i] = true;
            m[i, 6] = i % 2 == 0; r[i, 6] = true;
        }
    }

    private static readonly int[][] AlignTable =
    {
        Array.Empty<int>(), Array.Empty<int>(),
        new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 },
        new[] { 6, 30 }, new[] { 6, 34 }, new[] { 6, 22, 38 },
        new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
    };

    private static void PlaceAlignment(bool[,] m, bool[,] r, int n, int version)
    {
        var pos = AlignTable[version];
        foreach (var cy in pos)
            foreach (var cx in pos)
            {
                // 跳过与定位图形重叠的位置
                if ((cx == 6 && cy == 6) || (cx == 6 && cy == n - 7) || (cx == n - 7 && cy == 6)) continue;
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                    {
                        int px = cx + x, py = cy + y;
                        if (px < 0 || py < 0 || px >= n || py >= n) continue;
                        bool on = Math.Max(Math.Abs(x), Math.Abs(y)) != 1;
                        m[py, px] = on;
                        r[py, px] = true;
                    }
            }
    }

    private static void PlaceFormatReserve(bool[,] r, int n)
    {
        for (int i = 0; i <= 8; i++) { r[8, i] = true; r[i, 8] = true; }
        for (int i = 0; i < 8; i++) { r[8, n - 1 - i] = true; r[n - 1 - i, 8] = true; }
        r[n - 8, 8] = true;
    }

    private static void PlaceVersionReserve(bool[,] r, int n)
    {
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 3; j++)
            {
                r[n - 11 + j, i] = true;
                r[i, n - 11 + j] = true;
            }
    }

    private static void PlaceData(bool[,] m, bool[,] r, int n, List<bool> bits)
    {
        int idx = 0, col = n - 1;
        bool upward = true;
        while (col > 0)
        {
            if (col == 6) col--;
            for (int i = 0; i < n; i++)
            {
                int row = upward ? n - 1 - i : i;
                for (int c = 0; c < 2; c++)
                {
                    int cc = col - c;
                    if (r[row, cc]) continue;
                    m[row, cc] = idx < bits.Count && bits[idx];
                    idx++;
                }
            }
            upward = !upward;
            col -= 2;
        }
    }

    private static void PlaceFormat(bool[,] m, int n, int mask)
    {
        int data = (0 << 3) | mask; // 纠错 M = 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ (((rem >> 9) & 1) * 0x537);
        int fmt = ((data << 10) | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++) m[8, i] = ((fmt >> i) & 1) == 1;
        m[8, 7] = ((fmt >> 6) & 1) == 1;
        m[8, 8] = ((fmt >> 7) & 1) == 1;
        m[7, 8] = ((fmt >> 8) & 1) == 1;
        for (int i = 9; i <= 14; i++) m[14 - i, 8] = ((fmt >> i) & 1) == 1;

        for (int i = 0; i <= 7; i++) m[8, n - 1 - i] = ((fmt >> i) & 1) == 1;
        for (int i = 8; i <= 14; i++) m[n - 15 + i, 8] = ((fmt >> i) & 1) == 1;
        m[n - 8, 8] = true;
    }

    private static void ApplyMask(bool[,] m, bool[,] r, int n, int mask)
    {
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                if (r[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (y / 2 + x / 3) % 2 == 0,
                    5 => (x * y) % 2 + (x * y) % 3 == 0,
                    6 => ((x * y) % 2 + (x * y) % 3) % 2 == 0,
                    _ => ((x + y) % 2 + (x * y) % 3) % 2 == 0,
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    private static byte[] ReedSolomon(byte[] data, int ecCount)
    {
        var gen = GeneratorPolynomial(ecCount);
        var result = new byte[ecCount];
        foreach (var b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, ecCount - 1);
            result[ecCount - 1] = 0;
            for (int i = 0; i < ecCount; i++)
                result[i] ^= Multiply(gen[i], (byte)factor);
        }
        return result;
    }

    private static byte[] GeneratorPolynomial(int degree)
    {
        var poly = new byte[] { 1 };
        for (int i = 0; i < degree; i++)
        {
            var next = new byte[poly.Length + 1];
            for (int j = 0; j < poly.Length; j++)
            {
                next[j] ^= poly[j];
                next[j + 1] ^= (byte)Multiply(poly[j], Exp(i));
            }
            poly = next;
        }
        return poly.Skip(1).ToArray();
    }

    private static readonly byte[] ExpTable = BuildExp();
    private static readonly byte[] LogTable = BuildLog();

    private static byte[] BuildExp()
    {
        var t = new byte[512];
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            t[i] = (byte)x;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) t[i] = t[i - 255];
        return t;
    }

    private static byte[] BuildLog()
    {
        var t = new byte[256];
        for (int i = 0; i < 255; i++) t[ExpTable[i]] = (byte)i;
        return t;
    }

    private static byte Exp(int i) => ExpTable[i];
    private static byte Multiply(byte a, byte b)
        => a == 0 || b == 0 ? (byte)0 : ExpTable[LogTable[a] + LogTable[b]];
}
