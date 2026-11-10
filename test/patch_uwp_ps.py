# -*- coding: utf-8 -*-
"""UWP 图标回退：WindowsApps 受保护 → 用 PowerShell 读取（无需提权，只需读取权限检查）。
如果仍失败，退回到系统通用图标，保证 UI 不空白。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\WindowService.cs"
s = io.open(P, encoding="utf-8").read()

anchor = "    /// <summary>按包名查找 UWP 包目录里的 logo 并转 PNG（含 scale 变体择优）。</summary>"
assert anchor in s

new_method = r'''    /// <summary>通过 PowerShell 读取 UWP 包目录里的 logo（绕过 WindowsApps 的 ACL 限制）。</summary>
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
            DebugLog.Write($"UWP(PowerShell) 图标命中: {packageName}");
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UWP(PowerShell) 失败 {packageName}: {ex.Message}");
            return null;
        }
    }

'''
s = s.replace(anchor, new_method + anchor)

# 在 ExtractUwpLogoByPackageName 末尾失败前调用 PowerShell 回退
old_tail = '''        catch (Exception ex) { DebugLog.Write($"UWP 图标失败 {packageName}: {ex.Message}"); }
        return null;
    }'''
new_tail = '''        catch (Exception ex) { DebugLog.Write($"UWP 图标失败 {packageName}: {ex.Message}"); }

        // 受保护目录读取失败 → 走 PowerShell 通道
        return ExtractUwpLogoViaPowerShell(packageName);
    }'''
assert old_tail in s
s = s.replace(old_tail, new_tail, 1)

io.open(P, "w", encoding="utf-8").write(s)
print("PowerShell 回退已加入")
