# -*- coding: utf-8 -*-
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

# 移除可能残留的残缺语音 JS 块（上一次 shell 转义污染）
bad_start = "/* ========= 语音输入（Web Speech API） ========= */"
if bad_start in s:
    start = s.find(bad_start)
    end = s.find("/* ========= 设置 ========= */", start)
    s = s[:start] + s[end:]

voice_js = r'''
/* ========= 语音输入（Web Speech API） ========= */
var recog = null, listening = false;
$("voiceBtn").addEventListener("click", function () {
  var SR = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SR) { toast("此浏览器不支持语音识别（建议 Chrome）"); return; }
  if (listening) {
    listening = false;
    try { recog.stop(); } catch (e) {}
    this.style.color = ""; this.style.borderColor = "";
    buzz("tap");
    return;
  }
  recog = new SR();
  recog.lang = "zh-CN";
  recog.continuous = true;
  recog.interimResults = false;
  recog.onresult = function (ev) {
    var txt = "";
    for (var i = ev.resultIndex; i < ev.results.length; i++) {
      if (ev.results[i].isFinal) txt += ev.results[i][0].transcript;
    }
    if (txt) { send({ t: "text", value: txt }); buzz("left"); toast("已输入：" + txt); }
  };
  recog.onerror = function (e) { toast("语音识别错误：" + e.error); };
  recog.onend = function () {
    if (listening) { try { recog.start(); } catch (e) {} }
  };
  try {
    recog.start();
    listening = true;
    this.style.color = "var(--accent)";
    this.style.borderColor = "var(--accent)";
    buzz("page");
    toast("正在听…再次点击停止");
  } catch (e) { toast("启动失败：" + e.message); }
});
'''

marker = "/* ========= 设置 ========= */"
assert marker in s, "marker missing"
s = s.replace(marker, voice_js + "\n" + marker, 1)
io.open(P, "w", encoding="utf-8").write(s)
print("voice JS 已写入，长度", len(s))
