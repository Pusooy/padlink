# -*- coding: utf-8 -*-
"""把所有 UWP 启动命令改成 C# 逐字字符串 @"" 形式，彻底避开转义问题。"""
import io
import re

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\VolumeService.cs"
s = io.open(P, encoding="utf-8").read()

# 1) 匹配 ("名称", "shell:AppsFolder\...") 并改成 @ 前缀
def repl(m):
    name, target = m.group(1), m.group(2)
    return '("%s", @"%s")' % (name, target)

s = re.sub(r'\("([^"]+)",\s*"shell:AppsFolder\\+"?\s*([^"]*)"\)', repl, s)

# 兼容已经坏掉的行：("画图", "shell:AppsFolder\Microsoft.Paint_...!App"),
lines = s.split("\n")
fixed = []
for ln in lines:
    if "shell:AppsFolder" in ln and '"shell:AppsFolder' in ln and '@"shell:AppsFolder' not in ln:
        # 提取 name 与 aumid
        m = re.search(r'\("([^"]+)",\s*"shell:AppsFolder\\*(.+?)"\)', ln)
        if m:
            indent = re.match(r"\s*", ln).group(0)
            ln = '%s("%s", @"shell:AppsFolder\\%s"),' % (indent, m.group(1), m.group(2))
    fixed.append(ln)
s = "\n".join(fixed)

# 2) 去掉重复反斜杠（@"..." 内只需一个）
s = s.replace('@"shell:AppsFolder\\\\', '@"shell:AppsFolder\\')

# 3) prefix 用逐字字符串
s = re.sub(r'const string prefix = [^;]+;',
           'const string prefix = @"shell:AppsFolder\\\\";', s)
io.open(P, "w", encoding="utf-8").write(s)

for line in s.split("\n"):
    if "AppsFolder" in line:
        print(line.strip())
