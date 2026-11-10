using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace PadLink.Launcher;

/// <summary>
/// PadLink 单文件启动器。
/// WinUI 3 无法真正单文件发布（需要同目录的 DLL/资源），因此本启动器内嵌整个 UI 目录的
/// payload.zip，首次运行时解压到 %LOCALAPPDATA%\PadLink\app，然后启动其中的 PadLink.UI.exe。
/// 版本变化时重新解压（带"先删后覆盖"的容错）。
/// </summary>
internal static class Program
{
    private const string PayloadResource = "PadLink.Launcher.payload.zip";
    private const string VersionMarker = "version.txt";
    private const string UiExe = "PadLink.UI.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var appDir = AppDir();
            var uiExe = Path.Combine(appDir, UiExe);

            // ---- 判断是否需要解压 ----
            if (!File.Exists(uiExe) || NeedUpdate(appDir))
            {
                if (!Extract(appDir))
                {
                    Fail("解压失败，请检查磁盘空间或权限。");
                    return 1;
                }
            }

            if (!File.Exists(uiExe))
            {
                Fail($"未找到 {UiExe}，安装可能不完整。");
                return 1;
            }

            // ---- 启动 UI ----
            var psi = new ProcessStartInfo(uiExe)
            {
                UseShellExecute = false,
                WorkingDirectory = appDir,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            Fail("启动失败：" + ex.Message);
            return 2;
        }
    }

    private static string AppDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PadLink", "app");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>比较嵌入 payload 的版本标记与已解压的版本。</summary>
    private static bool NeedUpdate(string appDir)
    {
        try
        {
            var marker = Path.Combine(appDir, VersionMarker);
            if (!File.Exists(marker)) return true;

            var installed = File.ReadAllText(marker).Trim();
            var packaged = ReadEmbeddedVersion();
            return !string.Equals(installed, packaged, StringComparison.Ordinal);
        }
        catch { return true; }
    }

    private static string ReadEmbeddedVersion()
    {
        try
        {
            using var zip = OpenPayload();
            if (zip == null) return "";
            var entry = zip.GetEntry(VersionMarker);
            if (entry == null) return "";
            using var sr = new StreamReader(entry.Open());
            return sr.ReadToEnd().Trim();
        }
        catch { return ""; }
    }

    private static ZipArchive? OpenPayload()
    {
        var asm = typeof(Program).Assembly;
        var stream = asm.GetManifestResourceStream(PayloadResource);
        return stream == null ? null : new ZipArchive(stream, ZipArchiveMode.Read);
    }

    /// <summary>解压 payload 到 appDir（先尝试清空，失败则逐文件覆盖）。</summary>
    private static bool Extract(string appDir)
    {
        try
        {
            // 1) 尝试清空（可能因锁定失败）
            TryClear(appDir);

            // 2) 解压
            using var zip = OpenPayload();
            if (zip == null)
            {
                Fail("程序包损坏：缺少内嵌 payload。");
                return false;
            }

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;   // 目录项
                var target = Path.Combine(appDir,
                    entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(target);
                if (dir != null) Directory.CreateDirectory(dir);
                try
                {
                    entry.ExtractToFile(target, overwrite: true);
                }
                catch (IOException)
                {
                    // 单个文件被占用（如正在运行的 DLL）：跳过，使用已有版本
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Fail("解压异常：" + ex.Message);
            return false;
        }
    }

    private static void TryClear(string appDir)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(f); } catch { }
                }
                foreach (var d in Directory.EnumerateDirectories(appDir, "*", SearchOption.AllDirectories)
                             .OrderByDescending(x => x.Length))
                {
                    try { Directory.Delete(d, false); } catch { }
                }
                return;
            }
            catch { Thread.Sleep(400); }
        }
    }

    private static void Fail(string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, "PadLink", 0x00000010);
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
