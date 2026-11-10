# -*- coding: utf-8 -*-
"""IconService 支持 uwp:<AUMID> 前缀，直接用 AUMID 找包目录取 logo。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\WindowService.cs"
s = io.open(P, encoding="utf-8").read()

# GetForExe 入口先判断 uwp: 前缀
old = '''    public static byte[]? GetForExe(string exePath)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(exePath, out var cached)) return cached;'''
new = '''    public static byte[]? GetForExe(string exePath)
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
            if (Cache.TryGetValue(exePath, out var cached)) return cached;'''
assert old in s
s = s.replace(old, new)

# 新增按包名（不含 _8wekyb3d8bbwe 后缀全名也可）查找
anchor = "    private static IntPtr SendMessageTimeoutIcon(IntPtr hWnd)"
assert anchor in s
s = s.replace(anchor, r'''    /// <summary>按包名查找 UWP 包目录里的 logo 并转 PNG（含 scale 变体择优）。</summary>
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
                    DebugLog.Write($"UWP 图标命中: {packageName} → {Path.GetFileName(logoPath)}");
                    return ms.ToArray();
                }
            }
        }
        catch (Exception ex) { DebugLog.Write($"UWP 图标失败 {packageName}: {ex.Message}"); }
        return null;
    }

''' + anchor)
io.open(P, "w", encoding="utf-8").write(s)
print("IconService 已支持 uwp: 前缀")
