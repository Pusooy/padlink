using PadLink.Core;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PadLink.Interop;

public static class NetworkService
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
public sealed class DiscoverListener : IDisposable
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
        catch (Exception ex) { Log.Info($"discover bind fail: {ex.Message}"); }
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
public static class AppLauncher
{
    private static string ConfigPath => PadLinkConstants.AppsConfigPath;

    /// <summary>手机端应用页：合并 apps.json 自定义 + 开始菜单程序。</summary>
    public static List<object> ListApps()
    {
        return ProgramLauncher.Search("", 200);
    }

    private sealed class AppEntry
    {
        public string Name { get; set; } = "";
        public string Cmd { get; set; } = "";
    }

    private sealed class AppsFile
    {
        public List<AppEntry> Apps { get; set; } = new();
    }

    /// <summary>读取 apps.json。旧版文件键名大小写不一（Apps/Name/Cmd vs apps/name/cmd），
    /// 必须大小写不敏感反序列化，否则 open_app 全部失效。</summary>
    private static List<AppEntry> LoadApps()
    {
        EnsureConfig();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        try
        {
            var f = JsonSerializer.Deserialize<AppsFile>(File.ReadAllText(ConfigPath), opts);
            if (f?.Apps is { Count: > 0 } list) return list;
        }
        catch { }
        try
        {
            // 兼容把数组直接写在根上的写法
            var list = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(ConfigPath), opts);
            if (list != null) return list;
        }
        catch { }
        return new List<AppEntry>();
    }

    private static List<object> ListConfiguredApps()
    {
        return LoadApps().Select(a => (object)new { name = a.Name, cmd = a.Cmd }).ToList();
    }

    public static void Open(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var hit = LoadApps().FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                Process.Start(new ProcessStartInfo(hit.Cmd) { UseShellExecute = true });
                return;
            }
            // apps.json 没有：开始菜单快捷方式兜底
            var target = ProgramLauncher.FindTarget(name);
            if (target != null)
            {
                if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    target = ResolveShortcut(target) ?? target;
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }
            Log.Info($"open_app: 未找到应用 {name}");
        }
        catch (Exception ex) { Log.Info($"open_app: {ex.Message}"); }
    }

    /// <summary>
    /// 按应用名解析可执行文件路径（用于图标提取）：
    /// apps.json → 开始菜单/PATH 程序表 → PATH 环境变量 → System32。
    /// </summary>
    public static string? ResolvePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // 1) apps.json 自定义项
        try
        {
            EnsureConfig();
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
            {
                if (string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    var cmd = a.GetProperty("cmd").GetString()!;
                    var resolved = ResolveCmd(cmd);
                    if (resolved != null) return resolved;
                }
            }
        }
        catch { }

        // 2) 开始菜单 / 内置程序表（ProgramLauncher 已建立缓存）
        try
        {
            var hit = ProgramLauncher.FindTarget(name);
            if (hit != null)
            {
                // .lnk 快捷方式：解析真实目标
                if (hit.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var target = ResolveShortcut(hit);
                    if (target != null) return target;
                }
                var resolved = ResolveCmd(hit);
                if (resolved != null) return resolved;
            }
        }
        catch { }

        // 3) 直接当作 exe 名在 PATH / System32 中查找
        var direct = ResolveCmd(name);
        if (direct != null) return direct;

        // 4) 仍未找到：可能是 UWP 包应用，返回 AUMID 标记（IconService 会解析包图标）
        var aumid = ProgramLauncher.FindAumid(name);
        return aumid == null ? null : "uwp:" + aumid;
    }

    /// <summary>解析 .lnk 快捷方式指向的真实 exe 路径。</summary>
    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            return string.IsNullOrEmpty(target) || !File.Exists(target) ? null : target;
        }
        catch { return null; }
    }

    private static string? ResolveCmd(string cmd)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(cmd);
            if (File.Exists(expanded)) return Path.GetFullPath(expanded);
            var sys = Environment.SystemDirectory;
            var candidate = Path.Combine(sys, expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? expanded : expanded + ".exe");
            return File.Exists(candidate) ? candidate : null;
        }
        catch { return null; }
    }

    /// <summary>给设置窗口用：返回 name/cmd 原始列表。</summary>
    public static List<object> ListAppsRaw()
    {
        return LoadApps().Select(a => (object)new { name = a.Name, cmd = a.Cmd }).ToList();
    }

    /// <summary>保存应用列表到 apps.json。</summary>
    public static void SaveApps(List<(string Name, string Cmd)> apps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"apps\": [");
        for (int i = 0; i < apps.Count; i++)
        {
            var name = JsonSerializer.Serialize(apps[i].Name);
            var cmd = JsonSerializer.Serialize(apps[i].Cmd);
            sb.Append("    { \"name\": ").Append(name).Append(", \"cmd\": ").Append(cmd).Append('}');
            sb.AppendLine(i < apps.Count - 1 ? "," : "");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        File.WriteAllText(ConfigPath, sb.ToString());
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
