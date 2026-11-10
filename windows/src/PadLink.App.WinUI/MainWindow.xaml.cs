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
    private readonly PadLinkSettings _settings;
    private readonly ShellViewModel _vm;
    private bool _reallyClose;

    public MainWindow(PadLinkSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Title = "PadLink";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _vm = new ShellViewModel(settings);
        ContentFrame.Navigate(typeof(OverviewPage), _vm);

        // 窗口尺寸
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
        }
        catch (Exception ex) { Log.Error("窗口尺寸设置失败", ex); }

        // 关闭 = 隐藏到托盘（除非真的退出）
        Closed += OnClosed;

        // 跟随系统主题
        ApplySystemTheme();
        RootGrid.ActualThemeChanged += (_, _) => { };
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
