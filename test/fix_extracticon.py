# -*- coding: utf-8 -*-
"""修正 ExtractIconEx 的 P/Invoke：必须是完整的 5 参数签名，
Win32 的 ExtractIconExW 不接受参数个数不同的重载。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\WindowService.cs"
s = io.open(P, encoding="utf-8").read()

# 1) 替换两个错误的声明为一个正确的完整签名
old_decls = '''    /// <summary>返回 exe 内图标数量（nIconIndex = -1 的特殊约定）。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern int ExtractIconExCount(string file);

    /// <summary>提取前 n 个大图标句柄。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern int ExtractIconExLarge(string file, IntPtr[] largeIcons, int count);'''
new_decl = '''    /// <summary>
    /// Win32 ExtractIconExW 完整签名。
    /// nIconIndex = -1 时返回图标总数；>=0 时按索引提取。
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex,
        IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);'''
assert old_decls in s, "旧声明未找到"
s = s.replace(old_decls, new_decl)

# 2) 修正调用点
s = s.replace("int count = ExtractIconExCount(exePath);",
              "int count = ExtractIcon(exePath, -1, null, null, 0);")
s = s.replace("int got = ExtractIconExLarge(exePath, handles, take);",
              "int got = ExtractIcon(exePath, 0, handles, null, take);")

io.open(P, "w", encoding="utf-8").write(s)
print("ExtractIconEx 签名已修正（shell32.dll / 5 参数）")
