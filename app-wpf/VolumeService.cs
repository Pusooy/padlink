using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PadLink;

/// <summary>Windows 主机音量控制：Core Audio API（IAudioEndpointVolume）。</summary>
internal static class VolumeService
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints();          // 占位，顺序必须保留
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int count);
        int SetMasterVolumeLevel(float level, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(int ch, float level, ref Guid ctx);
        int SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(int ch, out float level);
        int GetChannelVolumeLevelScalar(int ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    private static IAudioEndpointVolume? Endpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            // eRender = 0, eMultimedia = 1
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 1 /*CLSCTX_ALL*/, IntPtr.Zero, out var obj);
            return obj as IAudioEndpointVolume;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"音量接口失败: {ex.Message}");
            return null;
        }
    }

    public static (int level, bool muted)? GetState()
    {
        var ep = Endpoint();
        if (ep == null) return null;
        try
        {
            var ctx = Guid.Empty;
            _ = ctx;
            if (ep.GetMasterVolumeLevelScalar(out var scalar) != 0) return null;
            if (ep.GetMute(out var muted) != 0) return null;
            return ((int)Math.Round(scalar * 100), muted);
        }
        catch { return null; }
    }

    public static void SetLevel(int percent)
    {
        var ep = Endpoint(); if (ep == null) return;
        var ctx = Guid.Empty;
        ep.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx);
        if (percent > 0)
        {
            ep.GetMute(out var muted);
            if (muted) ep.SetMute(false, ref ctx);
        }
    }

    public static void ToggleMute()
    {
        var ep = Endpoint(); if (ep == null) return;
        var ctx = Guid.Empty;
        if (ep.GetMute(out var muted) != 0) return;
        ep.SetMute(!muted, ref ctx);
    }
}

/// <summary>程序搜索与启动：扫描开始菜单快捷方式 + PATH + 已装应用。</summary>
internal static class ProgramLauncher
{
    private sealed record Entry(string Name, string Target);

    private static List<Entry>? _cache;
    private static DateTime _cacheAt = DateTime.MinValue;

    public static List<object> Search(string query, int limit = 40)
    {
        var all = LoadAll();
        var q = (query ?? "").Trim();
        IEnumerable<Entry> hits = q.Length == 0
            ? all.Take(limit)
            : all.Where(e => e.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                 .OrderBy(e => e.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                 .ThenBy(e => e.Name.Length);
        return hits.Take(limit)
            .Select(e => (object)new { name = e.Name, cmd = e.Target })
            .ToList();
    }

    /// <summary>若目标为 UWP 的 AppsFolder 形式，返回其 AUMID（用于取包图标）。</summary>
    public static string? FindAumid(string name)
    {
        var t = FindTarget(name);
        if (t == null) return null;
        const string prefix = @"shell:AppsFolder\";
        return t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? t.Substring(prefix.Length) : null;
    }

    /// <summary>按名称查找启动目标（快捷方式路径或命令）。</summary>
    public static string? FindTarget(string name)
    {
        var hit = LoadAll().FirstOrDefault(
            e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return hit?.Target;
    }

    private static List<Entry> LoadAll()
    {
        if (_cache != null && (DateTime.Now - _cacheAt).TotalMinutes < 10) return _cache;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. 开始菜单快捷方式（最贴近用户认知）
        var starts = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        foreach (var root in starts)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (name.Length == 0) continue;
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains("卸载", StringComparison.OrdinalIgnoreCase)) continue;
                    map.TryAdd(name, lnk);
                }
            }
            catch { }
        }

        // 2. 系统常见程序（PATH 里能找到的）
        var common = new[]
        {
            ("记事本", "notepad.exe"),
            // 经典 Win32 程序优先（多数系统都有）
            ("画图", "mspaint.exe"),
            ("写字板", "write.exe"),
            ("命令提示符", "cmd.exe"),
            ("PowerShell", "powershell.exe"),
            ("任务管理器", "taskmgr.exe"),
            ("控制面板", "control.exe"),
            ("资源管理器", "explorer.exe"),
            ("文件资源管理器", "explorer.exe"),
            ("注册表编辑器", "regedit.exe"),
            ("字符映射表", "charmap.exe"),
            ("放大镜", "magnify.exe"),
            ("Edge 浏览器", "msedge.exe"),
            ("系统设置", "ms-settings:"),
            // UWP 包应用（若系统已安装，走 AppsFolder 协议）
            ("计算器", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            ("截图工具", @"shell:AppsFolder\Microsoft.ScreenSketch_8wekyb3d8bbwe!App"),
            ("照片", @"shell:AppsFolder\Microsoft.Windows.Photos_8wekyb3d8bbwe!App"),
            ("相机", @"shell:AppsFolder\Microsoft.WindowsCamera_8wekyb3d8bbwe!App"),
            ("时钟", @"shell:AppsFolder\Microsoft.WindowsAlarms_8wekyb3d8bbwe!App"),
            ("便笺", @"shell:AppsFolder\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App"),
            ("Microsoft Store", @"shell:AppsFolder\Microsoft.WindowsStore_8wekyb3d8bbwe!App"),
        };
        foreach (var (name, cmd) in common) map.TryAdd(name, cmd);

        // 3. apps.json 自定义项
        try
        {
            foreach (var a in AppLauncher.ListAppsRaw())
            {
                dynamic d = a;
                map.TryAdd((string)d.name, (string)d.cmd);
            }
        }
        catch { }

        _cache = map.Select(kv => new Entry(kv.Key, kv.Value))
                    .OrderBy(e => e.Name, StringComparer.CurrentCulture)
                    .ToList();
        _cacheAt = DateTime.Now;
        return _cache;
    }

    public static void Launch(string nameOrCmd)
    {
        if (string.IsNullOrWhiteSpace(nameOrCmd)) return;
        try
        {
            var match = LoadAll().FirstOrDefault(
                e => e.Name.Equals(nameOrCmd, StringComparison.OrdinalIgnoreCase));
            var cmd = match?.Target ?? nameOrCmd;
            Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
        }
        catch (Exception ex) { DebugLog.Write($"launch '{nameOrCmd}' 失败: {ex.Message}"); }
    }
}
