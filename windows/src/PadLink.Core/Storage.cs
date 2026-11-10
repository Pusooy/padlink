using System.Text.Json;
using System.Text.Json.Serialization;

namespace PadLink.Core;

/// <summary>线程安全的文件日志（带滚动，避免无限增长）。</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null)
        => Write("ERR ", ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = PadLinkConstants.LogPath;
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                        File.Move(path, path + ".1", overwrite: true);
                }
                catch { }
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败绝不影响主流程 */ }
    }
}

/// <summary>设置读写（原子写入）。</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PadLinkSettings Load()
    {
        try
        {
            var path = PadLinkConstants.SettingsPath;
            if (!File.Exists(path)) return new PadLinkSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PadLinkSettings>(json, Options) ?? new PadLinkSettings();
        }
        catch (Exception ex)
        {
            Log.Error("读取设置失败，使用默认值", ex);
            return new PadLinkSettings();
        }
    }

    public static void Save(PadLinkSettings settings)
    {
        try
        {
            var path = PadLinkConstants.SettingsPath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("保存设置失败", ex);
        }
    }
}

/// <summary>自定义应用项（apps.json）。</summary>
public sealed class AppEntryModel
{
    public string Name { get; set; } = "";
    public string Cmd { get; set; } = "";
}

/// <summary>apps.json 容器。</summary>
public sealed class AppsConfig
{
    public List<AppEntryModel> Apps { get; set; } = new();
}

/// <summary>apps.json 读写。</summary>
public static class AppsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<AppEntryModel> Load()
    {
        try
        {
            var path = PadLinkConstants.AppsConfigPath;
            if (!File.Exists(path))
            {
                var defaults = DefaultApps();
                Save(defaults);
                return defaults;
            }
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppsConfig>(json, Options);
            return cfg?.Apps ?? new List<AppEntryModel>();
        }
        catch (Exception ex)
        {
            Log.Error("读取 apps.json 失败", ex);
            return new List<AppEntryModel>();
        }
    }

    public static void Save(IEnumerable<AppEntryModel> apps)
    {
        try
        {
            var cfg = new AppsConfig { Apps = apps.ToList() };
            File.WriteAllText(PadLinkConstants.AppsConfigPath,
                JsonSerializer.Serialize(cfg, Options));
        }
        catch (Exception ex)
        {
            Log.Error("保存 apps.json 失败", ex);
        }
    }

    public static List<AppEntryModel> DefaultApps() => new()
    {
        new() { Name = "记事本", Cmd = "notepad.exe" },
        new() { Name = "计算器", Cmd = "calc.exe" },
        new() { Name = "文件资源管理器", Cmd = "explorer.exe" },
        new() { Name = "命令提示符", Cmd = "cmd.exe" },
        new() { Name = "任务管理器", Cmd = "taskmgr.exe" },
        new() { Name = "画图", Cmd = "mspaint.exe" },
        new() { Name = "浏览器", Cmd = "msedge.exe" },
    };
}
