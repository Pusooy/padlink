# -*- coding: utf-8 -*-
"""
修复自启注册表路径 bug：
单文件 launcher 场景下，UI 进程的 Environment.ProcessPath 是
%LOCALAPPDATA%\\PadLink\\app\\PadLink.UI.exe（解压出来的），
但开机自启应该指向用户实际持有的外层单文件 PadLink.exe。
launcher 会通过环境变量 PADLINK_LAUNCHER 把自身路径传给子进程。
"""
import io

# 1) Launcher 传递自身路径
P1 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Launcher\Program.cs"
s1 = io.open(P1, encoding="utf-8").read()

old1 = '''            var psi = new ProcessStartInfo(uiExe)
            {
                UseShellExecute = false,
                WorkingDirectory = appDir,
            };'''
new1 = '''            var psi = new ProcessStartInfo(uiExe)
            {
                UseShellExecute = false,
                WorkingDirectory = appDir,
            };
            // 把 launcher 自身路径传给 UI，用于正确的开机自启注册
            var selfPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(selfPath))
                psi.Environment["PADLINK_LAUNCHER"] = selfPath;'''
assert old1 in s1, "launcher psi 未匹配"
s1 = s1.replace(old1, new1)
io.open(P1, "w", encoding="utf-8").write(s1)
print("Launcher: 已传递 PADLINK_LAUNCHER")

# 2) StartupRegistration 优先用该环境变量
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.App.WinUI\Services\TrayIcon.cs"
s2 = io.open(P2, encoding="utf-8").read()

old2 = '''            if (enable)
            {
                var exe = Environment.ProcessPath ?? "";
                key.SetValue(ValueName, $"\\"{exe}\\" --tray");
                Log.Info($"已启用自启：{exe}");
            }'''
new2 = '''            if (enable)
            {
                var exe = ResolveLauncherPath();
                key.SetValue(ValueName, $"\\"{exe}\\" --tray");
                Log.Info($"已启用自启：{exe}");
            }'''
assert old2 in s2, "StartupRegistration 未匹配"
s2 = s2.replace(old2, new2)

# 3) 新增 ResolveLauncherPath
anchor = '''    public static bool IsEnabled()'''
assert anchor in s2
s2 = s2.replace(anchor, '''    /// <summary>
    /// 解析用于自启的可执行路径。
    /// launcher 场景下 Environment.ProcessPath 指向解压目录里的 PadLink.UI.exe，
    /// 但自启必须指向用户手上的单文件 PadLink.exe（launcher 通过环境变量告知）。
    /// </summary>
    private static string ResolveLauncherPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("PADLINK_LAUNCHER");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        // 非 launcher 场景（直接运行 PadLink.UI.exe）：用它自己
        return Environment.ProcessPath ?? "";
    }

''' + anchor)
io.open(P2, "w", encoding="utf-8").write(s2)
print("StartupRegistration: 已用 ResolveLauncherPath")
