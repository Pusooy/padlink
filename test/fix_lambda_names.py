# -*- coding: utf-8 -*-
"""全面修复 MonitorEnumProc lambda 的参数命名。"""
import io, re

for P in [
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs",
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs",
]:
    s = io.open(P, encoding="utf-8").read()
    # 把所有 `(hMon, ..., ref rect, ...)` lambda 参数改为全命名
    s = re.sub(
        r"\(hMon,\s*(?:_|\w+),\s*ref rect,\s*(?:_|\w+)\)",
        "(hMon, hdcMonitor, ref rect, dwData)",
        s)
    io.open(P, "w", encoding="utf-8").write(s)
    print(f"  {P.split(chr(92))[-1]}: fixed")
