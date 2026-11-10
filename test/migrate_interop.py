# -*- coding: utf-8 -*-
"""把迁移到 Interop 层的服务文件改成 PadLink.Interop 命名空间，并接上 Core 层
（DebugLog -> Log、AppSettings -> SettingsStore、apps.json -> AppsStore）。"""
import io
import os
import re

ROOT = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop"

FILES = [
    "InputService.cs", "WindowService.cs", "ScreenService.cs", "VolumeService.cs",
    "FileService.cs", "AppLauncher.cs", "WebSocketHandler.cs", "WebHostBuilder.cs",
]

for name in FILES:
    p = os.path.join(ROOT, name)
    s = io.open(p, encoding="utf-8").read()

    # 1) 命名空间
    s = re.sub(r"^namespace\s+PadLink(\.Wpf)?;", "namespace PadLink.Interop;", s, flags=re.M)
    s = s.replace("namespace PadLink.Wpf;", "namespace PadLink.Interop;")
    s = s.replace("namespace PadLink;", "namespace PadLink.Interop;")

    # 2) 引用 Core
    if "using PadLink.Core;" not in s:
        # 插到第一行 using 之前
        m = re.search(r"^using ", s, flags=re.M)
        if m:
            s = s[:m.start()] + "using PadLink.Core;\n" + s[m.start():]
        else:
            s = "using PadLink.Core;\n\n" + s

    # 3) DebugLog -> Log
    s = s.replace("DebugLog.Write(", "Log.Info(")
    # 移除 DebugLog 定义（WebSocketHandler 里那个）
    s = re.sub(
        r"/// <summary>轻量文件日志（进程无控制台）。</summary>\s*"
        r"internal static class DebugLog\s*\{.*?\n\}\n",
        "", s, flags=re.S)

    # 4) apps.json 路径改到 Core 常量
    s = s.replace(
        'private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "apps.json");',
        'private static string ConfigPath => PadLinkConstants.AppsConfigPath;')

    io.open(p, "w", encoding="utf-8").write(s)
    print(f"  {name}: namespace=PadLink.Interop, Log/Core 已接入")
