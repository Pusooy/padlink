# -*- coding: utf-8 -*-
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\UiKit.cs"
s = io.open(P, encoding="utf-8").read()

anchor = '    public const string Rocket = '
idx = s.find(anchor)
assert idx > 0
line_end = s.find("\n", idx)
rocket_line = s[idx:line_end]

BS = chr(92)  # 反斜杠
addition = (
    "\n"
    '    public const string Add = "' + BS + 'uE710";             // 加号\n'
    '    public const string Save = "' + BS + 'uE74E";            // 保存\n'
    '    public const string Delete = "' + BS + 'uE74D";          // 删除（垃圾桶）\n'
    '    public const string Edit = "' + BS + 'uE70F";            // 编辑\n'
    '    public const string Warning = "' + BS + 'uE7BA";         // 警告\n'
    '    public const string Globe = "' + BS + 'uE774";           // 网页\n'
)

if "public const string Add" not in s:
    s = s[:line_end] + addition + s[line_end:]
    io.open(P, "w", encoding="utf-8").write(s)
    print("已插入 6 个字形常量")
else:
    print("已存在，跳过")

for line in s.split("\n"):
    if ("Add = " in line or "Save = " in line or "Delete = " in line
            or "Rocket = " in line):
        print(" ", line.strip())
