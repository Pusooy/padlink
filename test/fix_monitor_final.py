# -*- coding: utf-8 -*-
"""
彻底修复：MonitorEnumProc delegate 用 IntPtr 替代 ref RECT，
在回调内部用 Marshal.PtrToStructure 解析。避免所有 ref/lambda 冲突。
"""
import io
import re

for P in [
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs",
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs",
]:
    s = io.open(P, encoding="utf-8").read()

    # 1) delegate 改为 IntPtr（无 ref）
    s = s.replace(
        "private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);",
        "private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr data);")

    # 2) 替换所有 lambda 回调为 _monCallback 调用
    #    找到并替换所有 EnumDisplayMonitors 调用
    #    匹配整个 lambda 块
    s = re.sub(
        r'EnumDisplayMonitors\(IntPtr\.Zero,\s*IntPtr\.Zero,\s*\([^)]+\)\s*=>\s*\{[^}]+\},\s*IntPtr\.Zero\);',
        'EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);',
        s, flags=re.S)

    # 3) 确保 RECT 解析辅助方法存在
    if "static RECT PtrToRect" not in s:
        s = s.replace(
            "    private delegate bool MonitorEnumProc(",
            '''    private static RECT PtrToRect(IntPtr ptr)
    {
        var r = new RECT();
        Marshal.PtrToStructure(ptr, r);
        return r;
    }

    private delegate bool MonitorEnumProc(''')
        # 需要一个解析后的 RECT 列表供后续使用

    # 4) 收集 RECT 的模式：改为 List<RECT> 存在
    if "_monRects" not in s:
        s = s.replace(
            "    private delegate bool MonitorEnumProc(",
            '''    private static readonly List<RECT> _monRects = new();
    private static readonly object _monLock = new();
    private static readonly MonitorEnumProc _monCallback = (hMon, hdc, lprc, data) =>
    {
        lock (_monLock) { _monRects.Add(PtrToRect(lprc)); }
        return true;
    };

    private delegate bool MonitorEnumProc(''')
    else:
        # _monRects 已存在但可能没有 _monCallback
        if "_monCallback" not in s:
            s = s.replace(
                "    private delegate bool MonitorEnumProc(",
                '''    private static readonly MonitorEnumProc _monCallback = (hMon, hdc, lprc, data) =>
    {
        lock (_monLock) { _monRects.Add(PtrToRect(lprc)); }
        return true;
    };

    private delegate bool MonitorEnumProc(''')

    # 5) 确保调用 _monCallback 后 _monRects 里有数据
    #    所有使用 `monitors` 的地方在调用后从 _monRects 复制
    #    ListMonitors 的部分
    s = s.replace(
        'lock (_monLock) { _monRects.Clear(); }\n            EnumDisplayMonitors',
        '_monRects.Clear();\n            EnumDisplayMonitors')

    io.open(P, "w", encoding="utf-8").write(s)
    name = P.split("\\")[-1]
    lambda_count = len(re.findall(r'ref rect', s))
    print(f"  {name}: ref rect 残留={lambda_count}, _monCallback={'有' if '_monCallback' in s else '无'}")
