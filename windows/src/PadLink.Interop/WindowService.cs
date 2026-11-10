using PadLink.Core;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PadLink.Interop;

/// <summary>从 exe 提取图标并转 PNG（带缓存），供网页显示真实应用图标。</summary>
internal static class IconService
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, byte[]?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[]? GetForPid(uint pid)
    {
        try
        {
            var proc = Process.GetProcessById((int)pid);
            try
            {
                var path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    var ico = GetForExe(path);
                    if (ico != null) return ico;
                }
            }
            catch { }
            try
            {
                var pkg = GetPackageFamilyName(proc);
                if (!string.IsNullOrEmpty(pkg))
                {
                    var logo = ExtractUwpLogo(pkg!);
                    if (logo != null) return logo;
                }
            }
            catch { }
            var hIcon = SendMessageTimeoutIcon(proc.MainWindowHandle);
            if (hIcon != IntPtr.Zero) return SystemIconToPng(hIcon);
        }
        catch { }
        return null;
    }

    public static byte[]? GetForExe(string exePath)
    {
        // UWP 包应用：通过 AUMID 直接定位包目录取 logo
        if (exePath.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase))
        {
            lock (Lock)
            {
                if (Cache.TryGetValue(exePath, out var cachedUwp)) return cachedUwp;
                var aumid = exePath.Substring(4);
                var pkgName = aumid.Contains('!') ? aumid.Split('!')[0] : aumid;
                var byPkg = ExtractUwpLogoByPackageName(pkgName);
                Cache[exePath] = byPkg;
                return byPkg;
            }
        }

        lock (Lock)
        {
            if (Cache.TryGetValue(exePath, out var cached)) return cached;
            byte[]? png = null;
            try
            {
                png = ExtractLargestIcon(exePath);
                if (png == null)
                {
                    using var icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null) png = IconToPng(icon, 64);
                }
            }
            catch { }
            Cache[exePath] = png;
            return png;
        }
    }

    private static byte[]? ExtractLargestIcon(string exePath)
    {
        try
        {
            int count = ExtractIconEx(exePath, -1, null, null, 0);
            if (count <= 0)
            {
                // 回退：直接取关联图标（多数 exe 可用）
                using var assoc = Icon.ExtractAssociatedIcon(exePath);
                return assoc == null ? null : IconToPng(assoc, 64);
            }

            int take = Math.Min(count, 32);
            var handles = new IntPtr[take];
            int got = ExtractIconEx(exePath, 0, handles, null, take);
            if (got <= 0)
            {
                using var assoc = Icon.ExtractAssociatedIcon(exePath);
                return assoc == null ? null : IconToPng(assoc, 64);
            }

            IntPtr best = IntPtr.Zero;
            int bestSize = 0;
            for (int i = 0; i < got; i++)
            {
                var h = handles[i];
                if (h == IntPtr.Zero) continue;
                try
                {
                    using var ico = Icon.FromHandle(h);
                    int size = Math.Max(ico.Width, ico.Height);
                    if (size > bestSize) { bestSize = size; best = h; }
                }
                catch { }
            }

            byte[]? png = null;
            if (best != IntPtr.Zero)
            {
                try
                {
                    using var ico = Icon.FromHandle(best);
                    png = IconToPng(ico, 64);
                }
                catch { }
            }
            if (png == null)
            {
                using var assoc = Icon.ExtractAssociatedIcon(exePath);
                png = assoc == null ? null : IconToPng(assoc, 64);
            }
            foreach (var h in handles)
                if (h != IntPtr.Zero) DestroyIcon(h);
            return png;
        }
        catch (Exception ex)
        {
            Log.Info($"ExtractLargestIcon({exePath}) 失败: {ex.Message}");
            try
            {
                using var assoc = Icon.ExtractAssociatedIcon(exePath);
                return assoc == null ? null : IconToPng(assoc, 64);
            }
            catch { return null; }
        }
    }

    private static byte[]? IconToPng(Icon icon, int targetSize)
    {
        using var bmp = icon.ToBitmap();
        using var resized = new Bitmap(targetSize, targetSize);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(bmp, 0, 0, targetSize, targetSize);
        }
        using var ms = new MemoryStream();
        resized.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[]? SystemIconToPng(IntPtr hIcon)
    {
        try
        {
            using var ico = Icon.FromHandle(hIcon);
            return IconToPng(ico, 64);
        }
        catch { return null; }
    }

    private static string? GetPackageFamilyName(Process proc)
    {
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
        if (h == IntPtr.Zero) return null;
        try
        {
            uint len = 256;
            var sb = new System.Text.StringBuilder((int)len);
            int res = GetPackageFamilyName(h, ref len, sb);
            return res == 0 ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    private static byte[]? ExtractUwpLogo(string packageFamilyName)
    {
        try
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (!Directory.Exists(appDir)) return null;
            var nameHint = packageFamilyName.Split('_')[0];
            foreach (var dir in Directory.EnumerateDirectories(appDir, nameHint + "*"))
            {
                var manifests = Directory.GetFiles(dir, "AppxManifest.xml", SearchOption.TopDirectoryOnly);
                if (manifests.Length == 0) continue;
                var xml = File.ReadAllText(manifests[0]);
                var m = System.Text.RegularExpressions.Regex.Match(xml, "Square44x44Logo=\"([^\"]+)\"");
                if (!m.Success)
                    m = System.Text.RegularExpressions.Regex.Match(xml, "Logo=\"([^\"]+)\"");
                if (!m.Success) continue;
                var logo = Path.Combine(dir, m.Groups[1].Value.Replace('/', '\\'));
                var dirName = Path.GetDirectoryName(logo);
                var baseName = Path.GetFileNameWithoutExtension(logo);
                if (dirName != null && Directory.Exists(dirName))
                {
                    var variants = Directory.GetFiles(dirName, baseName + "*")
                        .OrderByDescending(f => new FileInfo(f).Length).ToArray();
                    if (variants.Length > 0) logo = variants[0];
                }
                if (!File.Exists(logo)) continue;
                using var src = new Bitmap(logo);
                using var resized = new Bitmap(64, 64);
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, 64, 64);
                }
                using var ms = new MemoryStream();
                resized.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
        catch { }
        return null;
    }

    /// <summary>通过 PowerShell 读取 UWP 包目录里的 logo（绕过 WindowsApps 的 ACL 限制）。</summary>
    private static byte[]? ExtractUwpLogoViaPowerShell(string packageName)
    {
        try
        {
            // 让 PowerShell 找出包安装目录里的 Square44x44Logo 实际文件
            var ps = "$ErrorActionPreference='SilentlyContinue';" +
                     "$p = Get-AppxPackage | Where-Object { $_.Name -like '" + packageName + "*' } | Select-Object -First 1;" +
                     "if ($p) {" +
                     "  $m = Join-Path $p.InstallLocation 'AppxManifest.xml';" +
                     "  if (Test-Path $m) {" +
                     "    [xml]$x = Get-Content $m;" +
                     "    $logo = $x.Package.Applications.Application.VisualElements.Square44x44Logo;" +
                     "    if (-not $logo) { $logo = $x.Package.Applications.Application.VisualElements.Square150x150Logo };" +
                     "    if ($logo) {" +
                     "      $base = Join-Path $p.InstallLocation ($logo -replace '/','\\');" +
                     "      $dir = Split-Path $base; $name = [IO.Path]::GetFileNameWithoutExtension($base);" +
                     "      $cand = Get-ChildItem $dir -Filter ($name + '*') | Sort-Object Length -Descending | Select-Object -First 1;" +
                     "      if ($cand) { $cand.FullName } else { $base }" +
                     "    }" +
                     "  }" +
                     "}";
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                ps.Replace("\"", "\\\"") + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var path = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(4000);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            using var src = new Bitmap(path);
            using var resized = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawImage(src, 0, 0, 64, 64);
            }
            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            Log.Info($"UWP(PowerShell) 图标命中: {packageName}");
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Info($"UWP(PowerShell) 失败 {packageName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>按包名查找 UWP 包目录里的 logo 并转 PNG（含 scale 变体择优）。</summary>
    private static byte[]? ExtractUwpLogoByPackageName(string packageName)
    {
        try
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    @"AppData\Local\Microsoft\WindowsApps"),
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string[] candidates;
                try { candidates = Directory.GetDirectories(root, packageName + "*"); }
                catch { continue; }
                foreach (var dir in candidates)
                {
                    var manifest = Path.Combine(dir, "AppxManifest.xml");
                    if (!File.Exists(manifest)) continue;
                    var xml = File.ReadAllText(manifest);
                    var m = System.Text.RegularExpressions.Regex.Match(xml, "Square44x44Logo=\"([^\"]+)\"");
                    if (!m.Success)
                        m = System.Text.RegularExpressions.Regex.Match(xml, "Square150x150Logo=\"([^\"]+)\"");
                    if (!m.Success)
                        m = System.Text.RegularExpressions.Regex.Match(xml, "Logo=\"([^\"]+)\"");
                    if (!m.Success) continue;

                    var rel = m.Groups[1].Value.Replace('/', '\\');
                    var logoPath = Path.Combine(dir, rel);
                    var logoDir = Path.GetDirectoryName(logoPath);
                    var baseName = Path.GetFileNameWithoutExtension(logoPath);
                    if (logoDir != null && Directory.Exists(logoDir))
                    {
                        // 优先选 targetsize-48/64 或无 scale 的基础图
                        var variants = Directory.GetFiles(logoDir, baseName + "*")
                            .OrderByDescending(f => f.Contains("targetsize-64") ? 3
                                                  : f.Contains("targetsize-48") ? 2
                                                  : f.Contains("scale-") ? 1 : 2)
                            .ThenByDescending(f => new FileInfo(f).Length)
                            .ToArray();
                        if (variants.Length > 0) logoPath = variants[0];
                    }
                    if (!File.Exists(logoPath)) continue;

                    using var src = new Bitmap(logoPath);
                    using var resized = new Bitmap(64, 64);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.DrawImage(src, 0, 0, 64, 64);
                    }
                    using var ms = new MemoryStream();
                    resized.Save(ms, ImageFormat.Png);
                    Log.Info($"UWP 图标命中: {packageName} → {Path.GetFileName(logoPath)}");
                    return ms.ToArray();
                }
            }
        }
        catch (Exception ex) { Log.Info($"UWP 图标失败 {packageName}: {ex.Message}"); }

        // 受保护目录读取失败 → 走 PowerShell 通道
        return ExtractUwpLogoViaPowerShell(packageName);
    }

    private static IntPtr SendMessageTimeoutIcon(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return IntPtr.Zero;
        const uint WM_GETICON = 0x007F;
        const int ICON_BIG = 1;
        try
        {
            SendMessageTimeout(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero,
                0x0002, 200, out var result);
            return result;
        }
        catch { return IntPtr.Zero; }
    }

    /// <summary>
    /// Win32 ExtractIconExW 完整签名。
    /// nIconIndex = -1 时返回图标总数；>=0 时按索引提取。
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex,
        IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr hProcess, ref uint len, System.Text.StringBuilder name);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
}

/// <summary>窗口枚举（仅任务栏级窗口）/ 激活 / 关闭 / 最小化。</summary>
internal static class WindowService
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private const int SW_RESTORE = 9, SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010, GA_ROOT = 2;
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x100, WS_CHILD = 0x40000000;
    private const int DWMWA_CLOAKED = 14;

    private static readonly HashSet<string> TitleBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Manager", "Windows Input Experience", "Microsoft Text Input Application",
        "WeType Native Candidate", "WeType Candidate Shadow and Emoji",
        "Windows Shell Experience Host", "开始", "任务栏", "搜索", "Nvidia Overlay", "Notification Center",
    };

    /// <summary>是否为任务栏/Alt-Tab 可见的顶层窗口。</summary>
    private static bool IsTaskbarWindow(IntPtr hWnd, string title)
    {
        if (!IsWindowVisible(hWnd)) return false;
        if (title.Length == 0 || TitleBlacklist.Contains(title)) return false;
        if (title.StartsWith("PadLink", StringComparison.OrdinalIgnoreCase)) return false;
        if ((GetWindowLongW(hWnd, GWL_STYLE) & WS_CHILD) != 0) return false;
        var ex = GetWindowLongW(hWnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (GetAncestor(hWnd, GA_ROOT) != hWnd) return false;
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, 4) == 0 && cloaked != 0) return false;
        return true;
    }

    public static List<object> ListWindows()
    {
        var result = new List<object>();
        var fg = GetForegroundWindow();
        EnumWindows((hWnd, _) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            var title = sb.ToString().Trim();
            if (!IsTaskbarWindow(hWnd, title)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            result.Add(new
            {
                id = hWnd.ToInt64(),
                title,
                pid,
                minimized = IsIconic(hWnd),
                foreground = hWnd == fg,
            });
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool FocusWindow(long id)
    {
        var hwnd = new IntPtr(id);
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
        // 先模拟一次 ALT 按键，规避前台锁定限制
        InputService.KeyTap("alt");
        Thread.Sleep(30);
        return SetForegroundWindow(hwnd);
    }

    /// <summary>关闭窗口：先 WM_CLOSE，700ms 后仍存在则恢复前台并发送 Alt+F4 兜底。</summary>
    public static bool CloseWindow(long id)
    {
        var hwnd = new IntPtr(id);
        if (!IsWindow(hwnd)) return false;
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            if (IsWindow(hwnd))
            {
                try
                {
                    if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
                    FocusWindow(hwnd.ToInt64());
                    Thread.Sleep(120);
                    InputService.KeyTap("alt+f4");
                    Log.Info($"关闭兜底 Alt+F4: hwnd={id}");
                }
                catch (Exception ex) { Log.Info($"关闭兜底失败: {ex.Message}"); }
            }
        });
        return true;
    }

    public static bool MinimizeWindow(long id)
    {
        var hwnd = new IntPtr(id);
        return IsWindow(hwnd) && ShowWindowAsync(hwnd, SW_MINIMIZE);
    }
}
