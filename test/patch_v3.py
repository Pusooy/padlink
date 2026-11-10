# -*- coding: utf-8 -*-
"""index.html v3 补丁：拟态鼠标面板、分档震动、水印行为、默认全屏横屏。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app\wwwroot\index.html"
s = io.open(P, encoding="utf-8").read()

# 1. 移除旧鼠标栏
old_bar = '''  <div id="mousebar">
    <button class="mbtn" id="mLeft"><i data-lucide="mouse-pointer-2"></i>左键</button>
    <button class="mbtn" id="mMid"><i data-lucide="circle-dot"></i>中键</button>
    <button class="mbtn" id="mRight"><i data-lucide="mouse-pointer-click"></i>右键</button>
    <button class="mbtn" id="mBack"><i data-lucide="chevron-left"></i>后退</button>
    <button class="mbtn" id="mFwd"><i data-lucide="chevron-right"></i>前进</button>
    <button class="mbtn" id="mDrag"><i data-lucide="hand"></i>拖拽</button>
  </div>
  <div id="stage">'''
new_bar = '''  <div id="stage">'''
assert old_bar in s, "mousebar"
s = s.replace(old_bar, new_bar)

# 顶栏加鼠标按钮
s = s.replace(
    '<button class="tbtn" id="kbBtn"><i data-lucide="keyboard"></i>键盘</button>',
    '<button class="tbtn" id="mouseBtn"><i data-lucide="mouse"></i>鼠标</button>\n    <button class="tbtn" id="kbBtn"><i data-lucide="keyboard"></i>键盘</button>')

s = s.replace(
    '.tbtn { border:1px solid var(--keyline); background:var(--key); color:var(--text); border-radius:11px;\n  padding:8px 11px; font-size:13px; cursor:pointer; transition:.15s;\n  display:inline-flex; align-items:center; gap:5px; }',
    '.tbtn { border:1px solid var(--keyline); background:var(--key); color:var(--text); border-radius:11px;\n  padding:8px 9px; font-size:12.5px; cursor:pointer; transition:.15s;\n  display:inline-flex; align-items:center; gap:4px; flex-shrink:0; }')

# 2. 拟态鼠标面板
mouse_panel = u'''
<!-- 拟态鼠标面板 -->
<div class="panel" id="mousepanel">
  <div class="grab"></div>
  <h3>拟态鼠标 · 点按分区=按键 · 上下拖滚轮=滚动</h3>
  <div id="mouseMock">
    <svg id="mouseSvg" viewBox="0 0 200 300">
      <path id="mockBody" d="M100,10 C158,10 176,58 176,138 C176,228 148,290 100,290 C52,290 24,228 24,138 C24,58 42,10 100,10 Z"
            fill="var(--key)" stroke="var(--keyline)" stroke-width="2"/>
      <path id="mockLeft" d="M97,14 L97,118 L38,118 C30,90 28,60 34,40 C40,20 62,14 97,14 Z"
            fill="transparent" stroke="var(--keyline)" stroke-width="2"/>
      <path id="mockRight" d="M103,14 C138,14 160,20 166,40 C172,60 170,90 162,118 L103,118 Z"
            fill="transparent" stroke="var(--keyline)" stroke-width="2"/>
      <line x1="24" y1="120" x2="176" y2="120" stroke="var(--keyline)" stroke-width="2"/>
      <rect id="mockWheel" x="90" y="42" width="20" height="62" rx="10"
            fill="var(--accent)" opacity="0.85"/>
      <text id="mockLabel" x="100" y="205" text-anchor="middle" font-size="15" fill="var(--text2)">左键</text>
    </svg>
  </div>
</div>

<!-- 键盘面板 -->'''
assert "\n<!-- 键盘面板 -->" in s
s = s.replace("\n<!-- 键盘面板 -->", mouse_panel, 1)

s = s.replace("/* 键盘面板 */", """/* 拟态鼠标 */
#mouseMock { display:flex; justify-content:center; }
#mouseSvg { height: 46vh; max-height:300px; touch-action:none; }
#mockLeft.hot, #mockRight.hot { fill: var(--accent-soft); }
#mockWheel.hot { opacity: 1; }

/* 键盘面板 */""", 1)

# 3. 水印 CSS
s = s.replace(
    "line-height:1.7; opacity:.75; }",
    "line-height:1.7; opacity:.6; transition:opacity .4s; }\n#hint.dim { opacity:.06; }")

# 4a. 分档震动
s = s.replace(
    "function buzz(ms) { try { navigator.vibrate && navigator.vibrate(ms); } catch (e) {} }",
    """function buzz(ms) { try { navigator.vibrate && navigator.vibrate(ms); } catch (e) {} }
  // 精细分档震动：轻点/普通/重按/中键节奏/滚轮刻度/开启
  var VIB = { tap:10, click:16, heavy:24, wheelTick:5, middle:[8,30,8], open:[12,40,12,40,24] };""")

# 4b. 替换旧鼠标栏事件
old_mb = """  /* ---------- 鼠标五键 ---------- */
  $("mLeft").addEventListener("click", function () { send({ t:"click", btn:"left" }); buzz(12); });
  $("mMid").addEventListener("click", function () { send({ t:"click", btn:"middle" }); buzz(12); });
  $("mRight").addEventListener("click", function () { send({ t:"click", btn:"right" }); buzz(14); });
  $("mBack").addEventListener("click", function () { send({ t:"click", btn:"back" }); buzz(12); });
  $("mFwd").addEventListener("click", function () { send({ t:"click", btn:"forward" }); buzz(12); });
  // 拖拽：按住期间左键保持按下
  (function () {
    var b = $("mDrag");
    function down(e) {
      e.preventDefault(); e.stopPropagation();
      if (!connected()) return;
      send({ t:"down", btn:"left" }); buzz(16);
      b.classList.add("holding");
      b.addEventListener("pointerup", up); b.addEventListener("pointercancel", up);
      b.setPointerCapture && e.pointerId != null && b.setPointerCapture(e.pointerId);
    }
    function up(e) {
      send({ t:"up", btn:"left" });
      b.classList.remove("holding");
      b.removeEventListener("pointerup", up); b.removeEventListener("pointercancel", up);
    }
    b.addEventListener("pointerdown", down);
  })();"""
new_mb = """  /* ---------- 拟态鼠标 ---------- */
  $("mouseBtn").addEventListener("click", function () {
    var show = !$("mousepanel").classList.contains("show");
    $("mousepanel").classList.toggle("show", show);
    kbpanel.classList.remove("show"); dock.classList.remove("show");
    this.classList.toggle("active", show);
    if (show) buzz(VIB.open);
  });
  function mockPress(el, fn) {
    el.addEventListener("touchstart", function (e) {
      e.preventDefault();
      el.classList.add("hot");
      if (fn.down) fn.down(e);
    }, { passive:false });
    el.addEventListener("touchmove", function (e) {
      e.preventDefault();
      if (fn.move) fn.move(e);
    }, { passive:false });
    var end = function (e) {
      e.preventDefault();
      el.classList.remove("hot");
      if (fn.up) fn.up(e);
    };
    el.addEventListener("touchend", end, { passive:false });
    el.addEventListener("touchcancel", end, { passive:false });
    el.addEventListener("mousedown", function (e) { el.classList.add("hot"); if (fn.down) fn.down(e); });
    el.addEventListener("mouseup", function (e) { el.classList.remove("hot"); if (fn.up) fn.up(e); });
  }
  mockPress($("mockLeft"), { down: function () { send({ t:"click", btn:"left" }); buzz(VIB.click); } });
  mockPress($("mockRight"), { down: function () { send({ t:"click", btn:"right" }); buzz(VIB.heavy); } });
  // 滚轮：纯点按=中键；上下拖=滚轮滚动
  (function () {
    var lastY = null, acc = 0, dragged = false;
    var wheel = $("mockWheel");
    mockPress(wheel, {
      down: function (e) {
        lastY = (e.touches && e.touches[0]) ? e.touches[0].clientY : e.clientY;
      },
      move: function (e) {
        if (lastY === null) return;
        var y = (e.touches && e.touches[0]) ? e.touches[0].clientY : e.clientY;
        var dy = (y - lastY) * SCROLL * 1.5;
        if (Math.abs(dy) > 0.4) {
          send({ t:"scroll", dx:0, dy:dy });
          dragged = true;
          acc += Math.abs(dy);
          if (acc > 30) { buzz(VIB.wheelTick); acc = 0; }
        }
        lastY = y;
      },
      up: function () { lastY = null; }
    });
    wheel.addEventListener("touchstart", function () { dragged = false; }, { passive:false });
    wheel.addEventListener("touchend", function (e) {
      e.preventDefault();
      wheel.classList.remove("hot");
      if (!dragged) { buzz(VIB.middle); send({ t:"click", btn:"middle" }); }
      lastY = null;
    }, { passive:false });
  })();"""
assert old_mb in s, "旧鼠标栏事件"
s = s.replace(old_mb, new_mb)

# 4d. 水印行为
old_touchstart = """    var ids = Object.keys(touches);
    if (ids.length === 1) padMoveLast = { x:e.changedTouches[0].clientX, y:e.changedTouches[0].clientY };
    else if (ids.length === 2) { padMoveLast = null; scrollTouch = e.changedTouches[0].identifier; scrollAccum = 0; }
    hint.style.display = "none";
  }, { passive:false });"""
new_touchstart = """    var ids = Object.keys(touches);
    if (ids.length === 1) padMoveLast = { x:e.changedTouches[0].clientX, y:e.changedTouches[0].clientY };
    else if (ids.length === 2) { padMoveLast = null; scrollTouch = e.changedTouches[0].identifier; scrollAccum = 0; }
    if (watermarkOn) hint.classList.add("dim");
    clearTimeout(hintTm);
  }, { passive:false });"""
assert old_touchstart in s, "touchstart 水印"
s = s.replace(old_touchstart, new_touchstart)

old_up1 = """    if (Object.keys(touches).length === 0) { padMoveLast = null; scrollTouch = null; }
  }, { passive:false });
  pad.addEventListener("touchcancel", function (e) {"""
new_up1 = """    if (Object.keys(touches).length === 0) { padMoveLast = null; scrollTouch = null; scheduleHintBack(); }
  }, { passive:false });
  pad.addEventListener("touchcancel", function (e) {"""
assert old_up1 in s, "touchend 水印"
s = s.replace(old_up1, new_up1)

s = s.replace("  var szLastY = null;", """  // 帮助水印：默认显示，触摸时隐去，闲置 4 秒后恢复
  var watermarkOn = localStorage.getItem("pl_watermark") !== "off";
  var hintTm = null;
  function scheduleHintBack() {
    clearTimeout(hintTm);
    hintTm = setTimeout(function () { if (watermarkOn) hint.classList.remove("dim"); }, 4000);
  }
  scheduleHintBack();

  var szLastY = null;""", 1)

# 4e. 设置区水印开关
s = s.replace("""  <div class="setRow">
    <label>滚动速度</label>
    <input type="range" id="sScroll" min="1" max="4" step="0.5">
    <span class="val" id="sScrollVal"></span>
  </div>""", """  <div class="setRow">
    <label>滚动速度</label>
    <input type="range" id="sScroll" min="1" max="4" step="0.5">
    <span class="val" id="sScrollVal"></span>
  </div>
  <div class="setRow">
    <label>帮助水印</label>
    <input type="range" id="wmToggle" min="0" max="1" step="1">
    <span class="val" id="wmVal"></span>
  </div>""")
s = s.replace("""  sScroll.value = SCROLL; $("sScrollVal").textContent = SCROLL.toFixed(1);""",
"""  sScroll.value = SCROLL; $("sScrollVal").textContent = SCROLL.toFixed(1);
  var wmSlider = $("wmToggle");
  function renderWm() {
    wmSlider.value = watermarkOn ? 1 : 0;
    $("wmVal").textContent = watermarkOn ? "开" : "关";
    hint.style.display = watermarkOn ? "flex" : "none";
    if (watermarkOn && !Object.keys(touches).length) hint.classList.remove("dim");
  }
  wmSlider.addEventListener("input", function () {
    watermarkOn = this.value === "1";
    localStorage.setItem("pl_watermark", watermarkOn ? "on" : "off");
    renderWm();
  });
  renderWm();""")

# 4f. 连接手势内请求全屏+锁横屏
s = s.replace(
    '$("connectBtn").addEventListener("click", function () { connect(ipin.value, false); });',
    """  function goFullscreen() {
    try {
      if (!document.fullscreenElement && document.documentElement.requestFullscreen) {
        document.documentElement.requestFullscreen().then(function () {
          try { screen.orientation && screen.orientation.lock && screen.orientation.lock("landscape").catch(function(){}); } catch (e) {}
        }).catch(function(){});
      }
    } catch (e) {}
  }
  $("connectBtn").addEventListener("click", function () {
    goFullscreen();
    connect(ipin.value, false);
  });""")

# 4g. 触控板震动分档
s = s.replace('send({ t:"click", btn:"left" }); buzz(12);', 'send({ t:"click", btn:"left" }); buzz(VIB.tap);')
s = s.replace('send({ t:"click", btn:"right" }); buzz(14); ripple(pad.clientWidth/2, pad.clientHeight/2);',
              'send({ t:"click", btn:"right" }); buzz(VIB.heavy); ripple(pad.clientWidth/2, pad.clientHeight/2);')
s = s.replace('if (scrollAccum > 30) { buzz(6); scrollAccum = 0; }', 'if (scrollAccum > 30) { buzz(VIB.wheelTick); scrollAccum = 0; }')

# 4h. 滚动区结束后恢复水印
s = s.replace("""    e.preventDefault(); szLastY = null; scrollzone.classList.remove("active");
  }, { passive:false });""",
"""    e.preventDefault(); szLastY = null; scrollzone.classList.remove("active"); scheduleHintBack();
  }, { passive:false });""")

io.open(P, "w", encoding="utf-8").write(s)
print("index.html v3 完成，共", len(s), "字符")
