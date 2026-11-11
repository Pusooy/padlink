# -*- coding: utf-8 -*-
"""一次性修复 Interop 项目所有编译错误。"""
import io
import re

# ==================== AudioMonitorService.cs ====================
P1 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs"
s = io.open(P1, encoding="utf-8").read()

# uint -> int: line 94 bufSize
s = s.replace("int bufSize = _format.nAvgBytesPerSec / 10;",
              "int bufSize = (int)(_format.nAvgBytesPerSec / 10);")

# long -> int: line 135 (WritePcm 里的 len 计算)
s = s.replace("int len = Math.Min(pcm.Length, _format.nAvgBytesPerSec / 5);",
              "int len = (int)Math.Min(pcm.Length, _format.nAvgBytesPerSec / 5);")

io.open(P1, "w", encoding="utf-8").write(s)
print("AudioMonitorService: 类型转换已修")

# ==================== ScreenService.cs ====================
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs"
s = io.open(P2, encoding="utf-8").read()

# 替换 EnumDisplayMonitors 调用为 MonitorService.ListMonitors()
old_monitor_call = '''            if (monitorIndex >= 0)
            {
                // 截取指定显示器
                var monitors = new List<(int Left, int Top, int Right, int Bottom)>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, AudioMonitorService._monCallback, IntPtr.Zero);
                if (monitorIndex < monitors.Count)'''
new_monitor_call = '''            if (monitorIndex >= 0)
            {
                // 截取指定显示器
                var monitorList = MonitorService.ListMonitors();
                var monitors = new List<(int Left, int Top, int Right, int Bottom)>();
                foreach (dynamic m in monitorList)
                    monitors.Add(((int)m.left, (int)m.top, (int)m.right, (int)m.bottom));
                if (monitorIndex < monitors.Count)'''

if old_monitor_call in s:
    s = s.replace(old_monitor_call, new_monitor_call)
    print("ScreenService: 显示器枚举已改为 MonitorService.ListMonitors()")
else:
    # 尝试另一种模式
    s = s.replace(
        "EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, AudioMonitorService._monCallback, IntPtr.Zero);",
        "var monitorList = MonitorService.ListMonitors();")
    print("ScreenService: 备选替换")

io.open(P2, "w", encoding="utf-8").write(s)

# ==================== WebSocketHandler.cs ====================
P3 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\WebSocketHandler.cs"
s = io.open(P3, encoding="utf-8").read()

# list_monitors 有 await 但可能在一个非 async 的 switch 里
# 让我看看当前结构
if 'case "list_monitors":' in s:
    # 如果在 Dispatch（非 async）中，移到 async Handle 方法
    # Dispatch 里的 list_monitors 需要移除，Handle 里的已有
    # 但 Dispatch 也有一个
    print("WebSocketHandler: 检查 list_monitors 位置")

