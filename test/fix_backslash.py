# -*- coding: utf-8 -*-
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\VolumeService.cs"
s = io.open(P, encoding="utf-8").read()

# 两个反斜杠 → 一个
s = s.replace("shell:AppsFolder\\\\", "shell:AppsFolder\\")
# 若已经是单反斜杠，保持不变（避免过度替换）
io.open(P, "w", encoding="utf-8").write(s)

# 打印相关行确认
for line in s.split("\n"):
    if "AppsFolder" in line:
        print(repr(line.strip()))
