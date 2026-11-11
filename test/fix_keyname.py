# -*- coding: utf-8 -*-
"""
修复 keyPress 的键名取值 bug：
ROWS 里单字母键写成 ["z"]（只有一项），此时 k[1] 是 undefined，应回退到 k[0]。
之前用 <input> 时这个分支没被走到，所以一直潜伏。
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.App.WinUI\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

# 1) keyPress 开头：安全取 name
old = "function keyPress(ri,ki){\n  var k=ROWS[ri][ki],name=k[1];buzz(\"tap\");"
new = """function keyPress(ri,ki){
  var k = ROWS[ri] && ROWS[ri][ki];
  if (!k) return;
  // 单字母键形如 ["z"]，双值键形如 ["<", ","] 或 ["⌫","backspace"]
  var name = (k.length > 1 && k[1] != null) ? k[1] : k[0];
  var label = k[0];
  buzz("tap");"""
assert old in s, "keyPress 开头未匹配"
s = s.replace(old, new)

# 2) 后面所有 k[0] / k[1] 的用法对齐：shift 上档字符应取 label 之外的上档
#    原逻辑：else if(shiftOn) ch=k[0]; —— 这里 k[0] 是上档字符，正确
#    但如果 name 已经回退到 k[0]，shift 场景需要看：双值键的 k[1] 是基础字符，k[0] 是上档
old_ch = """  var ch=name;
  if(ch.length===1){ if(/[a-z]/.test(ch)) ch=(shiftOn!==capsOn)?ch.toUpperCase():ch; else if(shiftOn) ch=k[0]; }"""
new_ch = """  var ch = name;
  if (ch.length === 1) {
    if (/[a-z]/.test(ch)) {
      // 字母：shift 与 caps 异或决定大小写
      ch = (shiftOn !== capsOn) ? ch.toUpperCase() : ch;
    } else if (shiftOn && k.length > 1) {
      // 符号键：shift 时取上档字符（k[0]）
      ch = k[0];
    }
  }"""
assert old_ch in s, "ch 计算未匹配"
s = s.replace(old_ch, new_ch)

io.open(P, "w", encoding="utf-8").write(s)
print("keyPress 键名取值已修复")

# 3) 同问题也可能存在于 buildKbd 的显示逻辑，检查一下
if "var cls=\"key\"+(k[2]" in s:
    print("buildKbd 使用 k[2] 作为宽度类，单值键 k[2] 为 undefined 是安全的（falsy）")
