using System.Text;
using System.Text.Json;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;

namespace PadLink;

/// <summary>WebSocket 消息分发：手机指令 → 各服务。</summary>
internal static class WebSocketHandler
{
    private static int _connected;
    public static int ConnectedCount => Volatile.Read(ref _connected);

    public static async Task Handle(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        Interlocked.Increment(ref _connected);
        try
        {
            var buffer = new byte[64 * 1024];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ctx.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                JsonDocument? doc = null;
                try { doc = JsonDocument.Parse(text); } catch { continue; }
                // Clone 使 JsonElement 脱离文档内存，供线程池使用
                var root = doc.RootElement.Clone();
                doc.Dispose();
                var type = root.TryGetProperty("t", out var tEl) ? tEl.GetString() : null;
                try
                {
                    if (type == "ping")
                    {
                        var ts = root.TryGetProperty("ts", out var tsEl) ? tsEl.GetInt64() : 0;
                        await Reply(ws, new { t = "pong", ts });
                        continue;
                    }
                    if (type == "list_windows")
                    {
                        await Reply(ws, new { t = "windows", list = WindowService.ListWindows() });
                        continue;
                    }
                    if (type == "list_apps")
                    {
                        await Reply(ws, new { t = "apps", list = AppLauncher.ListApps() });
                        continue;
                    }
                    // 输入类指令可能高频，丢到线程池执行，避免阻塞收包
                    await Task.Run(() => Dispatch(type, root));
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"处理 {type} 时异常: {ex.Message}");
                }
            }
        }
        catch (Exception ex) { DebugLog.Write($"WS 连接异常: {ex.GetType().Name}: {ex.Message}"); }
        finally { Interlocked.Decrement(ref _connected); DebugLog.Write($"WS 断开，当前连接数 {ConnectedCount}"); }
    }

    private static async Task Reply(WebSocket ws, object payload) =>
        await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)),
            WebSocketMessageType.Text, true, CancellationToken.None);

    private static void Dispatch(string? type, JsonElement e)
    {
        try
        {
            switch (type)
            {
                case "move":
                    InputService.MouseMove(GetD(e, "dx"), GetD(e, "dy"));
                    break;
                case "click":
                    InputService.MouseClick(GetS(e, "btn", "left"), GetI(e, "count", 1));
                    break;
                case "down":
                    InputService.MouseDown(GetS(e, "btn", "left"));
                    break;
                case "up":
                    InputService.MouseUp(GetS(e, "btn", "left"));
                    break;
                case "scroll":
                    InputService.Scroll(GetD(e, "dx"), GetD(e, "dy"));
                    break;
                case "text":
                    InputService.TypeText(GetS(e, "value", ""));
                    break;
                case "key":
                    InputService.KeyTap(GetS(e, "value", ""), GetI(e, "count", 1));
                    break;
                case "focus_window":
                    WindowService.FocusWindow(GetL(e, "id"));
                    break;
                case "close_window":
                    WindowService.CloseWindow(GetL(e, "id"));
                    break;
                case "min_window":
                    WindowService.MinimizeWindow(GetL(e, "id"));
                    break;
                case "open_app":
                    AppLauncher.Open(GetS(e, "name", ""));
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"dispatch {type}: {ex.Message}");
        }
    }

    private static double GetD(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.TryGetDouble(out var d) ? d : 0;
    private static long GetL(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.TryGetInt64(out var l) ? l : 0;
    private static int GetI(JsonElement e, string k, int def) =>
        e.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : def;
    private static string GetS(JsonElement e, string k, string def) =>
        e.TryGetProperty(k, out var v) ? v.GetString() ?? def : def;
}

/// <summary>轻量文件日志（进程无控制台）。</summary>
internal static class DebugLog
{
    private static readonly object Lock = new();
    public static void Write(string msg)
    {
        try
        {
            lock (Lock)
            {
                var dir = AppContext.BaseDirectory;
                File.AppendAllText(Path.Combine(dir, "padlink.log"),
                    $"{DateTime.Now:MM-dd HH:mm:ss} {msg}\n");
            }
        }
        catch { /* 日志失败不影响服务 */ }
    }
}
