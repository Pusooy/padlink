# -*- coding: utf-8 -*-
"""
键盘页重构：移除 <input>（会调起手机系统输入法），改为自建文本缓冲 + 只读显示区。
中文通过语音按钮输入，不依赖手机输入法。
"""
import io
import re

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.App.WinUI\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

# ---------- 1. HTML：替换键盘页头部 ----------
old_html = '''    <!-- 键盘 -->
    <section class="page" id="kbPage">
      <div style="display:flex;gap:8px;flex-shrink:0">
      <input id="kbInput" style="flex:1" placeholder="输入文字或使用下方键盘（支持中文）"
             autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false">
      <button id="voiceBtn" title="语音输入" style="width:46px;flex-shrink:0;border-radius:12px;
       border:1px solid var(--line);background:var(--surface);color:var(--text2);display:flex;
       align-items:center;justify-content:center"><i data-lucide="mic"></i></button>
      </div>
      <div id="kbGrid"></div>
    </section>'''

new_html = '''    <!-- 键盘（纯虚拟键盘，不唤起系统输入法） -->
    <section class="page" id="kbPage">
      <div id="kbBar">
        <div id="kbDisplay">
          <span id="kbText"></span><span id="kbCaret">|</span>
          <span id="kbPh">点下方按键输入 · 中文用右侧语音</span>
        </div>
        <button id="voiceBtn" title="语音输入中文"><i data-lucide="mic"></i></button>
      </div>
      <div id="kbGrid"></div>
    </section>'''

assert old_html in s, "键盘页 HTML 未匹配"
s = s.replace(old_html, new_html)

# ---------- 2. CSS：显示区样式 ----------
css_anchor = "/* 键盘页 */"
assert css_anchor in s
new_css = '''/* 键盘页 */
#kbBar{display:flex;gap:8px;flex-shrink:0;align-items:stretch}
#kbDisplay{flex:1;min-height:46px;max-height:96px;overflow-y:auto;padding:11px 14px;
 border-radius:12px;border:1px solid var(--line);background:var(--surface);
 font-size:15.5px;line-height:1.45;color:var(--text);word-break:break-all;
 -webkit-user-select:none;user-select:none;cursor:default}
#kbDisplay.empty #kbPh{display:inline}
#kbPh{display:none;color:var(--text3)}
#kbCaret{color:var(--accent);animation:kblink 1.1s step-end infinite;margin-left:1px}
@keyframes kblink{0%,50%{opacity:1}50.1%,100%{opacity:0}}
#voiceBtn{width:46px;flex-shrink:0;border-radius:12px;border:1px solid var(--line);
 background:var(--surface);color:var(--text2);display:flex;align-items:center;
 justify-content:center;transition:.15s}
#voiceBtn:active{background:var(--accentSoft);border-color:var(--accent);color:var(--accent)}
#voiceBtn.listening{background:var(--accentSoft);border-color:var(--accent);color:var(--accent);
 box-shadow:0 0 0 3px var(--accentSoft)}'''

s = s.replace(css_anchor, new_css, 1)
# 移除旧的 kbPage gap 规则中的 input 相关（保留 #kbGrid）
s = s.replace('#kbPage{gap:8px;padding-top:6px}\n', '#kbPage{gap:8px;padding-top:6px}\n', 1)

# ---------- 3. JS：重写键盘输入逻辑 ----------
old_js_start = s.find('var composing=false,flushTimer=null,kbInput=$("kbInput");')
assert old_js_start > 0
old_js_end = s.find('/* ========= 媒体/音量 ========= */')
assert old_js_end > old_js_start

new_js = r'''/* ========= 键盘：自建缓冲，不唤起系统输入法 ========= */
var kbBuf = "";          // 待发送文本
var kbFlushTimer = null;
var kbDisplay = $("kbDisplay");
var kbTextEl = $("kbText");

function kbRender(){
  kbTextEl.textContent = kbBuf;
  kbDisplay.classList.toggle("empty", kbBuf.length === 0);
  kbDisplay.scrollTop = kbDisplay.scrollHeight;
}

function kbScheduleFlush(){
  clearTimeout(kbFlushTimer);
  // 停顿 260ms 后整段发出并清空，保证电脑端字序正确且不闪烁
  kbFlushTimer = setTimeout(kbFlush, 260);
}

function kbFlush(){
  clearTimeout(kbFlushTimer); kbFlushTimer = null;
  if (!kbBuf) return;
  send({ t: "text", value: kbBuf });
  kbBuf = "";
  kbRender();
}

function kbAppend(ch){
  kbBuf += ch;
  kbRender();
  kbScheduleFlush();
}

function kbBackspace(){
  if (kbBuf.length > 0){
    // 按码点删除，正确处理中文/emoji
    kbBuf = Array.from(kbBuf).slice(0, -1).join("");
    kbRender();
    kbScheduleFlush();
  } else {
    // 缓冲已空 -> 直接给电脑发一个退格
    send({ t: "key", value: "backspace" });
  }
}

// 点击显示区可清空（长按也行，简单起见单击清空全部）
kbDisplay.addEventListener("click", function(){
  if (!kbBuf) return;
  var n = Array.from(kbBuf).length;
  kbBuf = "";
  kbRender();
  // 让电脑端也删掉已发出的这些字符（已 flush 的部分在电脑上）
  if (n > 0) { /* 已在电脑上的内容不追删，用户可再按退格 */ }
  buzz("tap");
});

/* 语音输入：识别结果追加到缓冲 */
'''

s = s[:old_js_start] + new_js + s[old_js_end:]

io.open(P, "w", encoding="utf-8").write(s)
print("键盘 HTML/CSS/JS 框架已替换")
