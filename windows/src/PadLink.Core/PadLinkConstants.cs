namespace PadLink.Core;

/// <summary>全局常量：端口、路径、应用名。</summary>
public static class PadLinkConstants
{
    public const string AppName = "PadLink";
    public const int DefaultPort = 8765;
    public const string MutexName = @"Local\PadLink.SingleInstance";
    public const string ShowEventName = @"Local\PadLink.Show";

    /// <summary>配置目录：%LOCALAPPDATA%\PadLink</summary>
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string AppsConfigPath => Path.Combine(DataDir, "apps.json");
    public static string LogPath => Path.Combine(DataDir, "padlink.log");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
}

/// <summary>应用设置（持久化到 settings.json）。</summary>
public sealed class PadLinkSettings
{
    public bool AutoStart { get; set; }
    public bool AutoOpenBrowser { get; set; }
    public bool ShowBalloon { get; set; } = true;
    public bool StartMinimized { get; set; }
    public int Port { get; set; } = PadLinkConstants.DefaultPort;
    /// <summary>依赖的 Windows 音量/截屏等能力开关（用户可关）。</summary>
    public bool EnableScreenMirror { get; set; } = true;
    public bool EnableFileManager { get; set; } = true;
}
