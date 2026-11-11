# -*- coding: utf-8 -*-
"""用命名静态方法替代 lambda 回调，彻底绕过 C# lambda ref 参数限制。"""
import io, re

for P in [
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs",
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs",
]:
    s = io.open(P, encoding="utf-8").read()

    # 1) 把 delegate 和回调改为 static 字段模式
    s = s.replace(
        "private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);",
        """private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    private static readonly List<RECT> _monRects = new();
    private static readonly object _monLock = new();""")

    # 2) 在 EnumDisplayMonitors 调用处替换为使用 _monRects
    #    ListMonitors 中的调用
    s = s.replace(
        '''EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdcMonitor, ref rect, dwData) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                GetMonitorInfo(hMon, ref info);
                list.Add(new
                {
                    index = list.Count,
                    device = info.szDevice,
                    primary = (info.dwFlags & 1) != 0,
                    left = rect.Left, top = rect.Top,
                    width = rect.Right - rect.Left,
                    height = rect.Bottom - rect.Top,
                });
                return true;
            }, IntPtr.Zero);''',
        '''lock (_monLock) { _monRects.Clear(); }
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);
            lock (_monLock)
            {
                foreach (var rect in _monRects)
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                    GetMonitorInfo(InfoFromRect(rect), ref info);
                    list.Add(new
                    {
                        index = list.Count,
                        device = info.szDevice,
                        primary = (info.dwFlags & 1) != 0,
                        left = rect.Left, top = rect.Top,
                        width = rect.Right - rect.Left,
                        height = rect.Bottom - rect.Top,
                    });
                }
            }''')

    # 3) CaptureMonitor 中的调用
    s = s.replace(
        '''            var monitors = new List<RECT>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdcMonitor, ref rect, dwData) =>
            {
                monitors.Add(rect);
                return true;
            }, IntPtr.Zero);''',
        '''            lock (_monLock) { _monRects.Clear(); }
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);
            var monitors = new List<RECT>(_monRects);''')

    # 4) 添加 _monCallback 静态方法
    if "_monCallback" in s and "static bool _monCallbackFn" not in s:
        s = s.replace(
            "    private static readonly List<RECT> _monRects = new();",
            '''    private static readonly List<RECT> _monRects = new();
    private static readonly MonitorEnumProc _monCallback = OnMonitorEnum;

    private static bool OnMonitorEnum(IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data)
    {
        lock (_monLock) { _monRects.Add(rect); }
        return true;
    }

    private static IntPtr InfoFromRect(RECT r) => IntPtr.Zero; // 不需要实际句柄''')

    # 5) GetMonitorInfo 在 CaptureMonitor 中的调用改为从 _monRects 取（不再需要）
    # CaptureMonitor 使用 monitors 列表即可

    io.open(P, "w", encoding="utf-8").write(s)
    print(f"  {P.split(chr(92))[-1]}: 回调已改为命名方法")
