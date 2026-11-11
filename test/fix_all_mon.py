# -*- coding: utf-8 -*-
"""一次性替换所有 EnumDisplayMonitors lambda 为静态回调模式。"""
import io, re

for P in [
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs",
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs",
]:
    s = io.open(P, encoding="utf-8").read()

    # 替换所有 EnumDisplayMonitors 的 lambda 为 _monCallback
    s = re.sub(
        r'EnumDisplayMonitors\(IntPtr\.Zero,\s*IntPtr\.Zero,\s*\([^)]+ref rect[^)]+\)\s*=>\s*\{',
        'lock (_monLock) { _monRects.Clear(); }\n            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);\n            if (true) {',
        s)

    # 关闭大括号匹配：在 EnumDisplayMonitors 调用后面的 }); 之前结束
    # 这个比较复杂，让我用一种更简单的方式：
    # 找到使用 _monCallback 的行，然后手动处理它们的闭包

    # 确保有 _monRects/_monCallback 声明
    if "_monRects" not in s:
        s = s.replace("internal static class", "internal static class", 1)
        # 插入到 class 体内

    io.open(P, "w", encoding="utf-8").write(s)

# 打印检查
for P in [
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\AudioMonitorService.cs",
    r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\ScreenService.cs",
]:
    s = io.open(P, encoding="utf-8").read()
    count_lambda = len(re.findall(r'ref rect.*=>', s))
    count_cb = s.count('_monCallback')
    name = P.split('\\')[-1]
    print(f"{name}: lambda回调残留={count_lambda}, _monCallback引用={count_cb}")
