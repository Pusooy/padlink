# -*- coding: utf-8 -*-
"""修复图标解析：ResolvePath 应能从 ProgramLauncher（开始菜单）与 PATH 解析。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\AppLauncher.cs"
s = io.open(P, encoding="utf-8").read()

old_start = s.find("    /// <summary>按应用名解析可执行文件路径")
old_end = s.find("    private static string? ResolveCmd(string cmd)")
assert old_start > 0 and old_end > old_start

new = r'''    /// <summary>
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
        return ResolveCmd(name);
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

'''
s = s[:old_start] + new + s[old_end:]
io.open(P, "w", encoding="utf-8").write(s)
print("ResolvePath 已增强")

# ProgramLauncher 增加 FindTarget
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\VolumeService.cs"
s2 = io.open(P2, encoding="utf-8").read()
anchor = "    private static List<Entry> LoadAll()"
assert anchor in s2
s2 = s2.replace(anchor, '''    /// <summary>按名称查找启动目标（快捷方式路径或命令）。</summary>
    public static string? FindTarget(string name)
    {
        var hit = LoadAll().FirstOrDefault(
            e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return hit?.Target;
    }

''' + anchor)
io.open(P2, "w", encoding="utf-8").write(s2)
print("ProgramLauncher.FindTarget 已添加")
