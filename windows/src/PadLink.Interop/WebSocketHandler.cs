using PadLink.Core;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;

namespace PadLink.Interop;

/// <summary>WebSocket 消息分发：手机指令 → 各服务。</summary>
public static class WebSocketHandler
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
            // 8KB 起步，长剪贴板等大消息按需扩容；常态小包不会重复分配
            var buffer = new byte[8 * 1024];
            while (ws.State == WebSocketState.Open)
            {
                int len = 0;
                WebSocketReceiveResult result;
                bool closed = false;
                do
                {
                    if (len == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                    result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer, len, buffer.Length - len), ctx.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close) { closed = true; break; }
                    len += result.Count;
                } while (!result.EndOfMessage);
                if (closed) break;

                var payload = new ReadOnlyMemory<byte>(buffer, 0, len);

                // ---- 紧凑高频协议（热路径，零堆分配）----
                // "M<dx>,<dy>" = 鼠标移动，"S<dx>,<dy>" = 滚轮
                // 直接在本循环同步执行：SendInput 是微秒级调用，切线程池反而更慢
                if (TryHandleCompact(buffer, len)) continue;

                // ---- 原有 JSON 协议（完全兼容安卓/旧版页面）----
                JsonDocument? doc = null;
                try
                {
                    // 直接解析 UTF8 字节，省掉一次 string 分配
                    doc = JsonDocument.Parse(payload);
                }
                catch { continue; }

                // Clone 使 JsonElement 脱离文档内存，供线程池使用
                var root = doc.RootElement.Clone();
                doc.Dispose();
                var type = root.TryGetProperty("t", out var tEl) ? tEl.GetString() : null;
                try
                {
                    if (type == "ping")
                    {
                        long ts = 0;
                        if (root.TryGetProperty("ts", out var tsEl) && tsEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            ts = tsEl.TryGetInt64(out var l) ? l : (long)tsEl.GetDouble();
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
                    if (type == "get_volume")
                    {
                        var st = VolumeService.GetState();
                        await Reply(ws, new { t = "volume", level = st?.level ?? 0, muted = st?.muted ?? false });
                        continue;
                    }
                    if (type == "get_clipboard")
                    {
                        await Reply(ws, new { t = "clipboard", text = ClipboardService.GetText() });
                        continue;
                    }
                    if (type == "get_sysinfo")
                    {
                        await Reply(ws, new { t = "sysinfo", info = SystemInfoService.Snapshot() });
                        continue;
                    }
                    if (type == "get_cursor")
                    {
                        var (nx, ny) = ScreenService.CursorNormalized();
                        await Reply(ws, new { t = "cursor", x = nx, y = ny });
                        continue;
                    }
                    if (type == "search_programs")
                    {
                        var q = root.TryGetProperty("q", out var qEl) ? qEl.GetString() ?? "" : "";
                        await Reply(ws, new { t = "programs", list = ProgramLauncher.Search(q) });
                        continue;
                    }

                    if (IsFastCommand(type))
                    {
                        // 输入类：同步执行。除了省掉线程池切换，更重要的是保证顺序——
                        // 若丢给线程池，「移动→点击」和「文字→回车」都可能被调度颠倒。
                        Dispatch(type, root);
                    }
                    else
                    {
                        // 低频重操作（启动程序 / 打开文件 / 关机等）：线程池执行，别阻塞收包
                        await Task.Run(() => Dispatch(type, root));
                    }
                }
                catch (Exception ex)
                {
                    Log.Info($"处理 {type} 时异常: {ex.Message}");
                }
            }
        }
        catch (Exception ex) { Log.Info($"WS 连接异常: {ex.GetType().Name}: {ex.Message}"); }
        finally { Interlocked.Decrement(ref _connected); Log.Info($"WS 断开，当前连接数 {ConnectedCount}"); }
    }

    /// <summary>紧凑协议快速路径。返回 true 表示已消费（无论执行成败），false 表示不是紧凑消息。</summary>
    private static bool TryHandleCompact(byte[] buffer, int len)
    {
        if (len < 4) return false;
        var first = buffer[0];
        if (first != (byte)'M' && first != (byte)'S') return false;
        if (!TryParsePair(buffer, len, out var dx, out var dy)) return false;
        try
        {
            if (first == (byte)'M') InputService.MouseMove(dx, dy);
            else InputService.Scroll(dx, dy);
        }
        catch (Exception ex) { Log.Info($"compact {(char)first} 异常: {ex.Message}"); }
        return true;
    }

    /// <summary>解析 "&lt;首字符&gt;&lt;dx&gt;,&lt;dy&gt;"。直接在 ASCII 字节上扫描：没有 UTF-8 解码、
    /// 没有 string/数组分配、也没有 double.TryParse 的格式化开销。</summary>
    private static bool TryParsePair(byte[] buffer, int len, out double dx, out double dy)
    {
        dx = dy = 0;
        if (len < 4 || len > 64) return false;
        var s = new ReadOnlySpan<byte>(buffer, 0, len);
        int comma = s.IndexOf((byte)',');
        if (comma < 2 || comma == len - 1) return false;
        var second = s[(comma + 1)..];
        if (second.IndexOf((byte)',') >= 0) return false; // 多个逗号：不是本协议
        if (!TryParseAsciiNumber(s[1..comma], out dx)) return false;
        if (!TryParseAsciiNumber(second, out dy)) return false;
        return true;
    }

    /// <summary>解析 -?\d+(\.\d+)? 形式的 ASCII 小数。超出这个形状就返回 false
    /// （客户端只会发这种；异常消息会退回 JSON 解析并最终被忽略）。</summary>
    private static bool TryParseAsciiNumber(ReadOnlySpan<byte> s, out double value)
    {
        value = 0;
        int i = 0, n = s.Length;
        if (n == 0) return false;

        bool neg = s[0] == (byte)'-';
        if (neg || s[0] == (byte)'+') i = 1;
        if (i >= n) return false;

        long intPart = 0;
        int digits = 0;
        while (i < n && (uint)(s[i] - (byte)'0') <= 9u)
        {
            intPart = intPart * 10 + (s[i++] - (byte)'0');
            if (++digits > 9) return false; // 位数异常，不猜
        }

        if (i == n) { value = neg ? -intPart : intPart; return true; }
        if (s[i] != (byte)'.') return false; // 不支持指数记号（客户端不发）

        i++;
        long frac = 0;
        double scale = 1;
        int fdigits = 0;
        while (i < n && (uint)(s[i] - (byte)'0') <= 9u)
        {
            frac = frac * 10 + (s[i++] - (byte)'0');
            scale *= 10;
            if (++fdigits > 9) return false;
        }
        if (i != n) return false;

        value = intPart + frac / scale;
        if (neg) value = -value;
        return true;
    }

    /// <summary>可在收包循环内同步执行的指令：都是微秒级 Win32 调用，走线程池反而增加延迟。
    /// 注意别把带 Thread.Sleep 的（如 focus_window 会等 30ms）放进来，否则会卡住移动。
    /// 同步执行还保证「移动 → 点击」的先后顺序，不会因线程池调度而颠倒。</summary>
    private static bool IsFastCommand(string? type) => type switch
    {
        "move" or "scroll" or "click" or "down" or "up" or "key" or "text"
            or "screen_move" or "screen_click" => true,
        _ => false,
    };

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
                case "set_volume":
                    VolumeService.SetLevel(GetI(e, "level", 50));
                    break;
                case "toggle_mute":
                    VolumeService.ToggleMute();
                    break;
                case "set_clipboard":
                    ClipboardService.SetText(GetS(e, "text", ""));
                    break;
                case "screen_click":
                    ScreenService.MoveToNormalized(GetD(e, "x"), GetD(e, "y"));
                    InputService.MouseClick(GetS(e, "btn", "left"));
                    break;
                case "screen_move":
                    ScreenService.MoveToNormalized(GetD(e, "x"), GetD(e, "y"));
                    break;
                case "open_file":
                    FileService.Open(GetS(e, "path", ""));
                    break;
                case "delete_file":
                    if (FileService.Delete(GetS(e, "path", "")))
                        Log.Info("已删除文件");
                    break;
                case "audio_start":
                    AudioPlaybackService.Start(GetI(e, "sampleRate", 16000));
                    break;
                case "audio_stop":
                    AudioPlaybackService.Stop();
                    break;
                case "audio_data":
                    // base64 PCM 数据解码并播放
                    var b64 = GetS(e, "pcm", "");
                    if (b64.Length > 0)
                    {
                        var bytes = Convert.FromBase64String(b64);
                        AudioPlaybackService.WritePcm(bytes);
                    }
                    break;
                case "set_monitor":
                    ScreenService.SetActiveMonitor(GetI(e, "index", -1));
                    break;
                case "power":
                    PowerService.Perform(GetS(e, "action", ""));
                    break;
                case "launch_program":
                    ProgramLauncher.Launch(GetS(e, "target", ""));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Info($"dispatch {type}: {ex.Message}");
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

