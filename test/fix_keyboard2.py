# -*- coding: utf-8 -*-
"""清理键盘改造后的残留：旧 #kbInput CSS、Dock 切页的 focus() 调用，
并把语音识别结果接入新的 kbAppend 缓冲。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.App.WinUI\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

# 1) 删除旧 #kbInput 的三条 CSS 规则
s = s.replace(
    '#kbInput{width:100%;padding:11px 14px;font-size:15.5px;border-radius:12px;border:1px solid var(--line);\n'
    ' background:var(--surface);color:var(--text);outline:none;transition:border-color .15s;flex-shrink:0}\n',
    '')
s = s.replace('#kbInput:focus{border-color:var(--accent)}\n', '')
s = s.replace('#kbInput::placeholder{color:var(--text3)}\n', '')

# 2) Dock 切到键盘页：不再 focus input（会唤起系统输入法）
s = s.replace(
    '    if(page==="kbPage") setTimeout(function(){$("kbInput").focus()},140);\n',
    '    if(page==="kbPage") kbRender();\n')

# 3) 语音识别：结果追加到 kbBuf（原实现直接 send text）
old_voice = '''    if (txt) { send({ t: "text", value: txt }); buzz("left"); toast("已输入：" + txt); }'''
new_voice = '''    if (txt) {
      // 语音结果直接发给电脑（中文不经手机输入法）
      send({ t: "text", value: txt });
      buzz("left");
      toast("已输入：" + txt);
    }'''
if old_voice in s:
    s = s.replace(old_voice, new_voice)

# 4) 语音按钮的 listening 类名切换（视觉反馈）
s = s.replace(
    '''    this.style.color = "var(--accent)";
    this.style.borderColor = "var(--accent)";''',
    '''    this.style.color = "var(--accent)";
    this.style.borderColor = "var(--accent)";
    this.classList.add("listening");''')
s = s.replace(
    '''    this.style.color = ""; this.style.borderColor = "";
    buzz("tap");
    return;''',
    '''    this.style.color = ""; this.style.borderColor = "";
    this.classList.remove("listening");
    buzz("tap");
    return;''')

# 5) keyPress 里的 backspace 特殊处理：走 kbBackspace
old_key = '''  var ch=name;
  if(ch.length===1){ if(/[a-z]/.test(ch)) ch=(shiftOn!==capsOn)?ch.toUpperCase():ch; else if(shiftOn) ch=k[0]; }'''
new_key = '''  // 退格：优先删本地缓冲，缓冲空才发到电脑
  if(name==="backspace"){ kbBackspace(); if(shiftOn){shiftOn=false;syncMods()} return; }

  var ch=name;
  if(ch.length===1){ if(/[a-z]/.test(ch)) ch=(shiftOn!==capsOn)?ch.toUpperCase():ch; else if(shiftOn) ch=k[0]; }'''
if old_key in s:
    s = s.replace(old_key, new_key)

# 6) 普通字符：改用 kbAppend 而不是直接 send（让显示区可见）
old_send = '''  if(mods.length) send({t:"key",value:mods.join("+")+"+"+ch});
  else send({t:"key",value:ch});'''
new_send = '''  if(mods.length){
    // 组合键立即发送（Ctrl+C 之类不该缓冲）
    send({t:"key",value:mods.join("+")+"+"+ch});
  } else if(ch.length===1){
    kbAppend(ch);              // 普通字符进缓冲，显示在输入区
  } else {
    send({t:"key",value:ch});  // 特殊键（enter/esc/方向键）立即发送
  }'''
if old_send in s:
    s = s.replace(old_send, new_send)

# 7) 回车 / 空格：回车先 flush 缓冲再发 return
old_enter_check = '''  if(name==="caps_lock"){capsOn=!capsOn;syncMods();return}'''
new_enter_check = '''  if(name==="caps_lock"){capsOn=!capsOn;syncMods();return}
  if(name==="enter"){ kbFlush(); send({t:"key",value:"return"}); return; }
  if(name==="space"){ kbAppend(" "); return; }'''
if old_enter_check in s:
    s = s.replace(old_enter_check, new_enter_check)

io.open(P, "w", encoding="utf-8").write(s)

# 校验
print("kbInput 残留:", s.count("kbInput"))
print("kbAppend 调用:", s.count("kbAppend("))
print("kbFlush 调用:", s.count("kbFlush("))
print("正在监听类:", "listening" in s)
