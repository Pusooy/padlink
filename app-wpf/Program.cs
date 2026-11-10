using System.IO;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using SystemIcons = System.Drawing.SystemIcons;
using Icon = System.Drawing.Icon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PadLink;

/// <summary>WPF 版入口：托盘 + Kestrel 服务 + 按需打开控制台窗口。</summary>
internal static class WpfProgram
{
    [STAThread]
    private static void Main()
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.Startup += (s, e) =>
        {
            var ctx = new TrayContext();
            ctx.StartServer();
            if (AppSettings.TrayBalloon) ctx.ShowStartupBalloon();
            if (AppSettings.AutoOpenPage) ctx.OpenControlPage();
            if (Environment.GetCommandLineArgs().Contains("--ui")) ctx.ShowWpfWindow();
            ctx.ShowMainWindowRequested += () => app.Dispatcher.Invoke(() => ctx.ShowWpfWindow());
        };
        app.Run();
    }
}

/// <summary>托盘 + 服务宿主（WPF 共用）。</summary>
internal sealed class TrayContext : IDisposable
{
    private NotifyIcon? _tray;
    private IHost? _host;
    private DiscoverListener? _discover;
    private PadLink.Wpf.MainWindow? _win;

    public event Action? ShowMainWindowRequested;

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制台", null, (s, e) => OpenMainWindow());
        menu.Items.Add("打开网页控制页", null, (s, e) => OpenControlPage());
        menu.Items.Add(new ToolStripSeparator());
        var autoItem = new ToolStripMenuItem("开机自启") { Checked = Autostart.IsEnabled() };
        autoItem.Click += (s, e) =>
        {
            Autostart.Toggle();
            autoItem.Checked = Autostart.IsEnabled();
        };
        menu.Items.Add(autoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) =>
        {
            Dispose();
            System.Windows.Application.Current?.Shutdown();
        });

        _tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? SystemIcons.Application,
            Text = "PadLink 服务",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (s, e) => OpenMainWindow();
    }

    public static string LocalIP => NetworkService.GetLocalIP();
    public static int Port => 8765;
    public static string BaseUrl => $"http://{LocalIP}:{Port}";

    public void StartServer()
    {
        try
        {
            DebugLog.Write($"启动服务 http://+:{Port}");
            _host = WebHostBuilder.Build($"http://+:{Port}", WebSocketHandler.Handle);
            _ = StartHostAsync();
            _discover = new DiscoverListener(LocalIP, Port);
            _discover.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"服务启动失败: {ex}");
            System.Windows.MessageBox.Show($"服务启动失败：{ex.Message}", "PadLink");
        }
    }

    private async Task StartHostAsync()
    {
        try
        {
            await _host!.StartAsync();
            DebugLog.Write("服务已就绪");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"StartAsync 失败: {ex}");
            System.Windows.MessageBox.Show($"服务启动失败：{ex.Message}", "PadLink");
        }
    }

    public void ShowStartupBalloon()
    {
        _tray?.ShowBalloonTip(2500, "PadLink 已启动", $"手机访问 {BaseUrl} 即可连接", ToolTipIcon.Info);
    }

    public void OpenControlPage()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BaseUrl) { UseShellExecute = true }); }
        catch { }
    }

    private void OpenMainWindow()
    {
        if (System.Windows.Application.Current == null)
        {
            ShowMainWindowCore();
            return;
        }
        System.Windows.Application.Current.Dispatcher.Invoke(ShowMainWindowCore);
    }

    private void ShowMainWindowCore()
    {
        if (_win == null || !_win.IsVisible)
        {
            _win = new PadLink.Wpf.MainWindow();
            _win.Closed += (s, e) => _win = null;
            _win.Show();
        }
        else
        {
            _win.Activate();
        }
    }

    public void ShowWpfWindow() => ShowMainWindowCore();

    public void Dispose()
    {
        _discover?.Dispose();
        _host?.StopAsync().Wait(1500);
        _host?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _win?.Close();
    }
}

/// <summary>设置项（HKCU\Software\PadLink）。</summary>
internal static class AppSettings
{
    private const string Key = @"Software\PadLink";

    public static bool AutoOpenPage
    {
        get => GetDword("AutoOpenPage") == 1;
        set => SetDword("AutoOpenPage", value ? 1 : 0);
    }

    public static bool TrayBalloon
    {
        get => GetDword("TrayBalloon") != 0; // 默认开启
        set => SetDword("TrayBalloon", value ? 1 : 0);
    }

    private static int GetDword(string name)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        var v = k?.GetValue(name);
        if (v is int i) return i;
        return name == "TrayBalloon" ? 1 : 0;
    }

    private static void SetDword(string name, int value)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
    }
}

/// <summary>开机自启（HKCU Run）。</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PadLink";

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void Toggle()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (IsEnabled()) key.DeleteValue(ValueName, false);
        else key.SetValue(ValueName, Environment.ProcessPath ?? Environment.ProcessPath ?? "");
    }
}
