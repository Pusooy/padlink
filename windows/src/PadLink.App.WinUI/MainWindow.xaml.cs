using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PadLink.App.ViewModels;
using PadLink.App.Views;
using PadLink.Core;
using WinRT.Interop;

namespace PadLink.App;

public sealed partial class MainWindow : Window
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly PadLinkSettings _settings;
    private readonly ShellViewModel _vm;
    private bool _reallyClose;

    public MainWindow(PadLinkSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Title = "PadLink";
        ExtendsContentIntoTitleBar = true;
        // 品牌头部即窗口拖动区（内容区保持完全可交互）
        SetTitleBar(BrandHeader);

        _vm = new ShellViewModel(settings);

        // 侧栏底部状态区直接绑定 ShellViewModel（连接状态 / 连接数）
        PaneFooterRoot.DataContext = _vm;
        _vm.Apps.CollectionChanged += (_, _) => UpdateAppsBadge();
        UpdateAppsBadge();

        ContentFrame.Navigate(typeof(OverviewPage), _vm);

        // 窗口尺寸：AppWindow.Resize 是物理像素。DPI 清单生效后（如 4K 200%）
        // 必须按 GetDpiForWindow 换算，否则逻辑尺寸缩水一半，布局走错分支
        // 且隐藏窗口的 0 尺寸测量趟会在 XAML 布局里触发除零（0xC000027B）。
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            double scale = GetDpiForWindow(hwnd) / 96.0;
            appWindow.Resize(new Windows.Graphics.SizeInt32(
                (int)Math.Round(1180 * scale), (int)Math.Round(800 * scale)));
        }
        catch (Exception ex) { Log.Error("窗口尺寸设置失败", ex); }

        // 关闭 = 隐藏到托盘（除非真的退出）
        Closed += OnClosed;

        // 跟随系统主题
        ApplySystemTheme();
    }

    private void ApplySystemTheme()
    {
        try
        {
            // 读系统应用主题偏好
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var light = key?.GetValue("AppsUseLightTheme") is int v && v == 1;
            RootGrid.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        }
        catch { RootGrid.RequestedTheme = ElementTheme.Dark; }
    }

    /// <summary>「应用管理」右侧徽章：显示自定义应用数量，为空时隐藏。</summary>
    private void UpdateAppsBadge()
    {
        var n = _vm.Apps.Count;
        AppsBadge.Value = n;
        AppsBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_reallyClose) return;
        args.Handled = true;   // 取消关闭，改为隐藏
        Log.Info("窗口关闭被拦截，隐藏到托盘");
        AppWindow.Hide();
    }

    /// <summary>真正的退出（托盘菜单调用）。</summary>
    public void CloseForExit()
    {
        _reallyClose = true;
        Close();
    }

    /// <summary>启动时隐藏到托盘（不激活窗口）。</summary>
    public void HideToTray()
    {
        try
        {
            AppWindow.Hide();
        }
        catch (Exception ex) { Log.Error("隐藏到托盘失败", ex); }
    }

    /// <summary>从托盘唤回窗口。</summary>
    public void ShowFromTray()
    {
        try
        {
            AppWindow.Show();
            Activate();
            this.SetForegroundWindow();
        }
        catch (Exception ex) { Log.Error("唤回窗口失败", ex); }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag?.ToString() ?? "overview";
        Type page = tag switch
        {
            "apps" => typeof(AppsPage),
            "clients" => typeof(ClientsPage),
            _ => typeof(OverviewPage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, _vm);
    }
}

internal static class WindowExtensions
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void SetForegroundWindow(this Window window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }
}
