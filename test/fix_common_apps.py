# -*- coding: utf-8 -*-
"""修正内置程序表：优先真实存在的经典 exe，UWP 作为多路径备选。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\VolumeService.cs"
s = io.open(P, encoding="utf-8").read()

start = s.find("        var common = new[]")
end = s.find("        };", start) + len("        };")
assert start > 0 and end > start

BS = chr(92)
new = '''        var common = new[]
        {
            ("记事本", "notepad.exe"),
            // 经典 Win32 程序优先（多数系统都有）
            ("画图", "mspaint.exe"),
            ("写字板", "write.exe"),
            ("命令提示符", "cmd.exe"),
            ("PowerShell", "powershell.exe"),
            ("任务管理器", "taskmgr.exe"),
            ("控制面板", "control.exe"),
            ("资源管理器", "explorer.exe"),
            ("注册表编辑器", "regedit.exe"),
            ("字符映射表", "charmap.exe"),
            ("放大镜", "magnify.exe"),
            ("Edge 浏览器", "msedge.exe"),
            ("系统设置", "ms-settings:"),
            // UWP 包应用（若系统已安装，走 AppsFolder 协议）
            ("计算器", @"shell:AppsFolder''' + BS + '''Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            ("截图工具", @"shell:AppsFolder''' + BS + '''Microsoft.ScreenSketch_8wekyb3d8bbwe!App"),
            ("照片", @"shell:AppsFolder''' + BS + '''Microsoft.Windows.Photos_8wekyb3d8bbwe!App"),
            ("相机", @"shell:AppsFolder''' + BS + '''Microsoft.WindowsCamera_8wekyb3d8bbwe!App"),
            ("时钟", @"shell:AppsFolder''' + BS + '''Microsoft.WindowsAlarms_8wekyb3d8bbwe!App"),
            ("便笺", @"shell:AppsFolder''' + BS + '''Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App"),
            ("Microsoft Store", @"shell:AppsFolder''' + BS + '''Microsoft.WindowsStore_8wekyb3d8bbwe!App"),
        };'''

s = s[:start] + new + s[end:]
io.open(P, "w", encoding="utf-8").write(s)
print("内置程序表已修正")

# AppLauncher.ResolvePath: 若 System32 有同名 exe 优先返回（覆盖 UWP 优先问题）
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\AppLauncher.cs"
s2 = io.open(P2, encoding="utf-8").read()
# 已在步骤 3 处理 direct，保持现状即可（apps.json → ProgramLauncher → PATH → uwp）
print("AppLauncher 解析顺序保持: apps.json → ProgramLauncher → PATH → UWP")
