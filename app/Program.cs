using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PadLink;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var context = new TrayContext();
        context.StartServer();
        Application.Run(context);
        context.StopServer();
    }
}

/// <summary>托盘常驻上下文：托盘图标 + 菜单 + Kestrel 服务。</summary>
internal sealed class TrayContext : ApplicationContext
{
    private NotifyIcon? _tray;
    private IHost? _host;
    private DiscoverListener? _discover;
    private SettingsForm? _settingsForm;

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制页", null, (s, e) => OpenControlPage());
        menu.Items.Add("连接信息", null, (s, e) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        var autoItem = new ToolStripMenuItem("开机自启") { Checked = Autostart.IsEnabled() };
        autoItem.Click += (s, e) =>
        {
            Autostart.Toggle();
            autoItem.Checked = Autostart.IsEnabled();
        };
        menu.Items.Add(autoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "PadLink 服务",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (s, e) => OpenControlPage();
    }

    private static Icon LoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }

    public static string LocalIP => NetworkService.GetLocalIP();
    public static int Port => 8765;
    public string BaseUrl => $"http://{LocalIP}:{Port}";

    public void StartServer()
    {
        try
        {
            DebugLog.Write($"启动服务，监听 http://+:{Port}，wwwroot={AppContext.BaseDirectory}");
            _host = WebHostBuilder.Build($"http://+:{Port}", HandleWs);
            _ = StartHostAsync();
            _discover = new DiscoverListener(LocalIP, Port);
            _discover.Start();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"服务启动失败: {ex}");
            MessageBox.Show($"服务启动失败：{ex.Message}", "PadLink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show($"服务启动失败：{ex.Message}", "PadLink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void StopServer()
    {
        _discover?.Dispose();
        _host?.StopAsync().Wait(2000);
        _host?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
    }

    private Task HandleWs(HttpContext ctx) => WebSocketHandler.Handle(ctx);

    private void OpenControlPage()
    {
        try { Process.Start(new ProcessStartInfo(BaseUrl) { UseShellExecute = true }); }
        catch { MessageBox.Show($"请手动打开：{BaseUrl}", "PadLink"); }
    }

    private void ShowSettings()
    {
        _settingsForm?.Dispose();
        _settingsForm = new SettingsForm(BaseUrl, WebSocketHandler.ConnectedCount);
        _settingsForm.Show();
    }
}

/// <summary>开机自启（HKCU Run 注册表，免管理员）。</summary>
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
        else key.SetValue(ValueName, Environment.ProcessPath ?? Application.ExecutablePath);
    }
}
