using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PadLink;

/// <summary>
/// 文件管理：目录浏览、文件预览（图片/文本）、下载、上传、打开。
/// 安全：仅允许访问用户目录 + 盘符根，拒绝系统敏感目录；所有路径做归一化校验。
/// </summary>
internal static class FileService
{
    private static readonly string[] DenyPrefixes =
    {
        @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
        @"C:\ProgramData", @"C:\$Recycle.Bin", @"C:\System Volume Information",
    };

    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".csv", ".yml", ".yaml", ".ini", ".cfg",
        ".cs", ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".html", ".css", ".sql", ".bat", ".ps1",
    };
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
    };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi",
    };
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".ogg",
    };

    /// <summary>常用入口（快捷方式列表）。</summary>
    public static List<object> Roots()
    {
        var list = new List<object>();
        void Add(string label, string path)
        {
            try { if (Directory.Exists(path)) list.Add(new { name = label, path }); } catch { }
        }
        Add("桌面", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add("下载", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        Add("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("图片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("视频", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add("音乐", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    list.Add(new { name = $"{d.Name.TrimEnd('\\')} ({d.VolumeLabel})", path = d.RootDirectory.FullName });
            }
            catch { }
        }
        return list;
    }

    private static bool IsAllowed(string fullPath)
    {
        foreach (var deny in DenyPrefixes)
            if (fullPath.StartsWith(deny, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>列目录。path 为空时返回根列表。</summary>
    public static object List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new { path = "", parent = (string?)null, roots = Roots(), entries = Array.Empty<object>() };

        string full;
        try { full = Path.GetFullPath(path); } catch { return new { error = "路径无效" }; }
        if (!IsAllowed(full)) return new { error = "该目录不允许访问" };
        if (!Directory.Exists(full)) return new { error = "目录不存在" };

        var entries = new List<object>();
        try
        {
            var di = new DirectoryInfo(full);
            foreach (var d in di.EnumerateDirectories().Take(400))
            {
                if (d.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                entries.Add(new { name = d.Name, path = d.FullName, dir = true, size = 0L, ext = "" });
            }
            foreach (var f in di.EnumerateFiles().Take(600))
            {
                if (f.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                entries.Add(new
                {
                    name = f.Name, path = f.FullName, dir = false,
                    size = f.Length, ext = f.Extension.TrimStart('.').ToLowerInvariant(),
                });
            }
        }
        catch (Exception ex) { return new { error = ex.Message }; }

        var parent = Directory.GetParent(full)?.FullName;
        if (parent != null && !IsAllowed(parent)) parent = null;
        return new { path = full, parent, entries };
    }

    /// <summary>读文本预览（限 400KB）。</summary>
    public static string ReadText(string path, int maxBytes = 400 * 1024)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!IsAllowed(full) || !File.Exists(full)) return "";
            using var fs = File.OpenRead(full);
            var len = (int)Math.Min(fs.Length, maxBytes);
            var buf = new byte[len];
            _ = fs.Read(buf, 0, len);
            var enc = DetectEncoding(buf);
            var text = enc.GetString(buf);
            if (fs.Length > maxBytes) text += "\n\n…（文件较大，仅显示前 400KB）";
            return text;
        }
        catch (Exception ex) { return "读取失败：" + ex.Message; }
    }

    private static Encoding DetectEncoding(byte[] buf)
    {
        if (buf.Length >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
            return new UTF8Encoding(false);
        // 简单 UTF-8 有效性检测，否则按 GBK 处理中文
        try
        {
            new UTF8Encoding(false, true).GetString(buf);
            return new UTF8Encoding(false);
        }
        catch
        {
            try { return Encoding.GetEncoding(936); } catch { return Encoding.UTF8; }
        }
    }

    public static bool IsText(string path) => TextExt.Contains(Path.GetExtension(path));
    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));
    public static bool IsVideo(string path) => VideoExt.Contains(Path.GetExtension(path));
    public static bool IsAudio(string path) => AudioExt.Contains(Path.GetExtension(path));

    public static string KindOf(string path)
    {
        var ext = Path.GetExtension(path);
        if (ImageExt.Contains(ext)) return "image";
        if (VideoExt.Contains(ext)) return "video";
        if (AudioExt.Contains(ext)) return "audio";
        if (TextExt.Contains(ext)) return "text";
        return "other";
    }

    public static void Open(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!IsAllowed(full)) { DebugLog.Write($"拒绝打开 {full}"); return; }
            Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
        }
        catch (Exception ex) { DebugLog.Write($"open 失败: {ex.Message}"); }
    }

    public static bool Delete(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!IsAllowed(full)) return false;
            if (File.Exists(full)) { File.Delete(full); return true; }
            if (Directory.Exists(full)) { Directory.Delete(full, false); return true; }
        }
        catch (Exception ex) { DebugLog.Write($"delete 失败: {ex.Message}"); }
        return false;
    }

    /// <summary>上传保存：写入指定目录（重名自动加序号）。</summary>
    public static string? Save(string dir, string fileName, Stream content)
    {
        try
        {
            var fullDir = Path.GetFullPath(dir);
            if (!IsAllowed(fullDir) || !Directory.Exists(fullDir)) return null;
            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "upload.bin";
            var target = Path.Combine(fullDir, safeName);
            var n = 1;
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName);
            while (File.Exists(target))
                target = Path.Combine(fullDir, $"{stem} ({n++}){ext}");
            using var fs = File.Create(target);
            content.CopyTo(fs);
            return target;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"上传失败: {ex.Message}");
            return null;
        }
    }

    public static string? ResolveForDownload(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return IsAllowed(full) && File.Exists(full) ? full : null;
        }
        catch { return null; }
    }

    public static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".pdf" => "application/pdf",
        ".txt" or ".md" or ".log" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
