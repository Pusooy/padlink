using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PadLink.Core;
using PadLink.Interop;

namespace PadLink.App.Services;

/// <summary>内嵌 Kestrel 服务宿主（HTTP + WebSocket），与 UI 同进程。</summary>
public static class ServerHost
{
    private static IHost? _host;
    private static DiscoverListener? _discover;

    public static void Start(int port)
    {
        try
        {
            _host = PadLink.Interop.WebHostBuilder.Build($"http://+:{port}", PadLink.Interop.WebSocketHandler.Handle);
            _ = StartAsync();
            _discover = new DiscoverListener(NetworkInfo.LocalIP, port);
            _discover.Start();
            Log.Info($"服务已启动，端口 {port}，IP {NetworkInfo.LocalIP}");
        }
        catch (Exception ex)
        {
            Log.Error("服务启动失败", ex);
        }
    }

    private static async Task StartAsync()
    {
        try
        {
            await _host!.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Kestrel StartAsync 失败", ex);
        }
    }

    public static void Stop()
    {
        try
        {
            _discover?.Dispose();
            _host?.StopAsync().Wait(TimeSpan.FromSeconds(2));
            _host?.Dispose();
        }
        catch (Exception ex) { Log.Error("停止服务失败", ex); }
        _host = null;
        _discover = null;
    }
}

/// <summary>网络信息。</summary>
public static class NetworkInfo
{
    public static string LocalIP
    {
        get
        {
            try
            {
                using var s = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram,
                    System.Net.Sockets.ProtocolType.Udp);
                s.Connect("8.8.8.8", 80);
                return (s.LocalEndPoint as System.Net.IPEndPoint)!.Address.ToString();
            }
            catch { return "127.0.0.1"; }
        }
    }
}
