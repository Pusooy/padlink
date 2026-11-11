# -*- coding: utf-8 -*-
"""修复 MonitorEnumProc lambda 的 ref 参数问题 + ScreenService 的多余变量。"""
import io
import re

# ---- AudioMonitorService.cs ----
P1 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs"
s1 = io.open(P1, encoding="utf-8").read()

# 替换所有 lambda 中的 _ 参数为命名参数
s1 = s1.replace(
    "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, ref rect, _) =>",
    "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, ref rect, data) =>")

# MonitorNormalized 中的调用也修正
s1 = s1.replace(
    "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, ref rect, _) =>\n        {\n            monitors.Add(rect); return true;\n        }, IntPtr.Zero);",
    "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, ref rect, data) =>\n        {\n            monitors.Add(rect); return true;\n        }, IntPtr.Zero);")

# 删除未使用的 hdc/data 参数引用
s1 = s1.replace("EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, ref rect, data) =>\n        {\n            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };",
                "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdcParam, ref rect, dataParam) =>\n        {\n            _ = hdcParam; _ = dataParam;\n            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };")

io.open(P1, "w", encoding="utf-8").write(s1)
print("AudioMonitorService lambdas fixed")

# ---- ScreenService.cs ----
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs"
s2 = io.open(P2, encoding="utf-8").read()

# 修 _tmpX/_tmpY 错误（未声明变量）
s2 = s2.replace("            _ = _tmpX; _ = _tmpY;\n            _tmpX = x; _tmpY = y;\n", "")
s2 = s2.replace("            vw = w; vh = h;\n            x = 0; y = 0;", "")

# lambda ref 参数修复
s2 = s2.replace(
    "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, ref rect, _) =>",
    "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdcParam, ref rect, dataParam) =>")

io.open(P2, "w", encoding="utf-8").write(s2)
print("ScreenService fixed")
