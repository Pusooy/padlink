# -*- coding: utf-8 -*-
"""
修键盘缓冲失效：kbBuf/kbDisplay/kbTextEl 等状态声明必须在使用之前。
当前 kbAppend/kbRender 定义在 keyPress 之后，且 kbBuf 用 var 声明在后面，
虽然函数声明会提升，但访问 kbTextEl（也是 var）时值为 undefined，
kbRender 里 kbTextEl.textContent 会抛 TypeError（被 forEach 里的隐式错误吞掉）。
解决：把整段键盘状态声明 + 辅助函数挪到 keyPress 之前。
"""
import io
import re

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.App.WinUI\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

# 1) 抽出当前的键盘缓冲代码块（从 var kbBuf 到 语音输入注释前）
start = s.find('/* ========= 键盘：自建缓冲，不唤起系统输入法 ========= */')
end = s.find('/* ========= 语音输入')
assert start > 0 and end > start, "键盘块未找到"
kb_block = s[start:end]

# 2) 从原位置移除
s = s[:start] + s[end:]

# 3) 插到 buildKbd 之前（keyPress 定义之前）
anchor = '(function buildKbd(){'
idx = s.find(anchor)
assert idx > 0, "buildKbd 未找到"
s = s[:idx] + kb_block + "\n" + s[idx:]

io.open(P, "w", encoding="utf-8").write(s)

# 4) 顺序校验
order = []
for name, pat in [
    ("kbBuf 声明", "var kbBuf"),
    ("kbAppend 定义", "function kbAppend"),
    ("buildKbd", "function buildKbd"),
    ("keyPress", "function keyPress"),
]:
    pos = s.find(pat)
    order.append((name, pos))

print("定义顺序：")
for n, p in order:
    print(f"  {n}: 位置 {p}")
ok = all(order[i][1] < order[i + 1][1] for i in range(len(order) - 1))
print("顺序正确" if ok else "❌ 顺序错误")
