using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PadLink;

internal static class NetworkService
{
    public static string GetLocalIP()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return (s.LocalEndPoint as IPEndPoint)!.Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}

/// <summary>UDP 广播发现：手机/其他设备广播 "PADLINK_DISCOVER"，本机回自己的地址。</summary>
internal sealed class DiscoverListener : IDisposable
{
    private UdpClient? _udp;
    private Task? _loop;
    private readonly string _ip;
    private readonly int _port;

    public DiscoverListener(string ip, int port)
    {
        _ip = ip;
        _port = port;
    }

    public void Start()
    {
        try
        {
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port + 1));
            _loop = Task.Run(LoopAsync);
        }
        catch (Exception ex) { DebugLog.Write($"discover bind fail: {ex.Message}"); }
    }

    private async Task LoopAsync()
    {
        while (_udp != null)
        {
            try
            {
                var res = await _udp.ReceiveAsync();
                var text = Encoding.UTF8.GetString(res.Buffer);
                if (text.Contains("PADLINK_DISCOVER"))
                {
                    var reply = JsonSerializer.Serialize(new { t = "padlink", ip = _ip, port = _port, name = Environment.MachineName });
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    await _udp.SendAsync(bytes, res.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException) { break; }
            catch { /* 忽略单个包错误 */ }
        }
    }

    public void Dispose() { try { _udp?.Dispose(); } catch { } }
}

/// <summary>应用启动器：apps.json 定义可从手机一键打开的应用。</summary>
internal static class AppLauncher
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "apps.json");

    public static List<object> ListApps()
    {
        EnsureConfig();
        var list = new List<object>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
            {
                list.Add(new
                {
                    name = a.GetProperty("name").GetString(),
                    cmd = a.GetProperty("cmd").GetString(),
                    icon = a.TryGetProperty("icon", out var i) ? i.GetString() : null,
                });
            }
        }
        catch (Exception ex) { DebugLog.Write($"list_apps: {ex.Message}"); }
        return list;
    }

    public static void Open(string name)
    {
        EnsureConfig();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
            {
                if (string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    var cmd = a.GetProperty("cmd").GetString()!;
                    Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
                    return;
                }
            }
            DebugLog.Write($"open_app: 未找到应用 {name}");
        }
        catch (Exception ex) { DebugLog.Write($"open_app: {ex.Message}"); }
    }

    private static void EnsureConfig()
    {
        if (File.Exists(ConfigPath)) return;
        File.WriteAllText(ConfigPath, """
            {
              "apps": [
                { "name": "记事本", "cmd": "notepad.exe" },
                { "name": "计算器", "cmd": "calc.exe" },
                { "name": "文件资源管理器", "cmd": "explorer.exe" },
                { "name": "命令提示符", "cmd": "cmd.exe" },
                { "name": "任务管理器", "cmd": "taskmgr.exe" },
                { "name": "画图", "cmd": "mspaint.exe" },
                { "name": "浏览器", "cmd": "msedge.exe" }
              ]
            }
            """);
    }
}
