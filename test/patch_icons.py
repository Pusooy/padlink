# -*- coding: utf-8 -*-
"""优化 IconService：高清提取 + UWP 回退。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\WindowService.cs"
s = io.open(P, encoding="utf-8").read()

start = s.find("internal static class IconService")
end = s.find("/// <summary>窗口枚举")
assert start > 0 and end > start

new = r'''internal static class IconService
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
        int count = ExtractIconEx(exePath, -1, null!, null!, 0);
        if (count <= 0) return null;
        int take = Math.Min(count, 32);
        var icons = new IntPtr[take];
        int got = ExtractIconEx(exePath, 0, icons, null!, take);
        if (got <= 0) return null;
        IntPtr best = IntPtr.Zero;
        int bestSize = 0;
        foreach (var h in icons)
        {
            if (h == IntPtr.Zero) continue;
            try
            {
                using var ico = Icon.FromHandle(h);
                var size = Math.Max(ico.Width, ico.Height);
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
        foreach (var h in icons) if (h != IntPtr.Zero) DestroyIcon(h);
        return png;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);

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

'''

s = s[:start] + new + s[end:]
io.open(P, "w", encoding="utf-8").write(s)
print("IconService 优化完成")
