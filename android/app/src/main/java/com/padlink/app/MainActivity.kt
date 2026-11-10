package com.padlink.app

import android.annotation.SuppressLint
import android.app.Activity
import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.text.Editable
import android.text.TextWatcher
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import kotlin.math.abs

/**
 * PadLink 原生安卓端：触控板 + 鼠标五键 + 键盘缓冲输入。
 * 震动使用 VibrationEffect 振幅级精细控制（网页端做不到的能力）。
 */
class MainActivity : Activity() {

    private lateinit var vibrator: Vibrator
    private lateinit var handler: Handler

    private val httpClient = OkHttpClient()
    private var ws: WebSocket? = null
    private var host = ""
    private var connected = false

    private var sens = 1.6f
    private var scrollSpeed = 2f

    private lateinit var statusDot: TextView
    private lateinit var statusText: TextView
    private lateinit var connectPage: LinearLayout
    private lateinit var mainPage: LinearLayout
    private lateinit var hostInput: EditText
    private lateinit var keyboardInput: EditText
    private lateinit var hintView: TextView
    private lateinit var padView: TouchpadView

    private var pingCount = 0

    // ---- 震动：振幅级精细控制 ----
    private fun buzz(ms: Long, amp: Int = 180) {
        if (Build.VERSION.SDK_INT >= 26) {
            vibrator.vibrate(VibrationEffect.createOneShot(ms, amp))
        } else {
            @Suppress("DEPRECATION") vibrator.vibrate(ms)
        }
    }
    private fun tickVib() = buzz(6, 90)
    private fun doubleVib() {
        if (Build.VERSION.SDK_INT >= 26) {
            vibrator.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 8, 40, 8), intArrayOf(0, 160, 0, 160), -1))
        } else buzz(16)
    }

    // ---- 生命周期 ----
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        handler = Handler(Looper.getMainLooper())
        vibrator = if (Build.VERSION.SDK_INT >= 31) {
            (getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
        } else {
            @Suppress("DEPRECATION") getSystemService(VIBRATOR_SERVICE) as Vibrator
        }
        hideSystemBars()
        requestedOrientation = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE

        buildUi()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemBars()
    }

    private fun hideSystemBars() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility =
                View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
        }
    }

    // ---- UI 构建（程序化，无 XML 布局） ----
    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()

    private fun bg(color: Int, radius: Float = 0f): GradientDrawable =
        GradientDrawable().apply { setColor(color); if (radius > 0) cornerRadius = dp(radius.toInt().toFloat()) }

    private fun buildUi() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.parseColor("#0e0f13"))
        }

        // ===== 连接页 =====
        connectPage = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }
        val title = TextView(this).apply {
            text = "PadLink"
            textSize = 32f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(Color.WHITE)
            gravity = Gravity.CENTER
        }
        val subtitle = TextView(this).apply {
            text = "先在电脑启动 PadLink 托盘程序\n手机与电脑连同一 WiFi"
            textSize = 13f
            setTextColor(Color.parseColor("#8b8f9a"))
            gravity = Gravity.CENTER
            setPadding(0, dp(10), 0, dp(20))
        }
        hostInput = EditText(this).apply {
            hint = "电脑 IP，如 192.168.1.100"
            textSize = 16f
            gravity = Gravity.CENTER
            setSingleLine()
            setTextColor(Color.WHITE)
            setHintTextColor(Color.parseColor("#6c7079"))
            background = bg(Color.parseColor("#16171d"), 12f)
            val lp = LinearLayout.LayoutParams(dp(320), ViewGroup.LayoutParams.WRAP_CONTENT)
            lp.gravity = Gravity.CENTER
            layoutParams = lp
            setPadding(dp(12), dp(12), dp(12), dp(12))
        }
        val connectBtn = Button(this).apply {
            text = "连 接"
            textSize = 16f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(Color.WHITE)
            background = bg(Color.parseColor("#3d7bff"), 12f)
            val lp = LinearLayout.LayoutParams(dp(320), dp(50))
            lp.gravity = Gravity.CENTER
            layoutParams = lp
            setOnClickListener {
                hideSystemBars()
                buzz(24, 220)
                connect(hostInput.text.toString().trim())
            }
        }
        connectPage.addView(title)
        connectPage.addView(subtitle)
        connectPage.addView(hostInput, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { gravity = Gravity.CENTER; leftMargin = dp(34); rightMargin = dp(34) })
        connectPage.addView(connectBtn, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50)).apply { gravity = Gravity.CENTER; leftMargin = dp(34); rightMargin = dp(34); topMargin = dp(16) })
        hostInput.layoutParams = hostInput.layoutParams // 保持
        root.addView(connectPage)

        // ===== 主界面 =====
        mainPage = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        }

        // 顶栏
        val topbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(14), dp(8), dp(14), dp(4))
        }
        statusDot = TextView(this).apply {
            text = "●"
            textSize = 12f
            setTextColor(Color.parseColor("#e05555"))
            setPadding(0, 0, dp(6), 0)
        }
        statusText = TextView(this).apply {
            text = "未连接"
            textSize = 13f
            setTextColor(Color.parseColor("#8b8f9a"))
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }
        val exitBtn = TextView(this).apply {
            text = "断开"
            textSize = 13f
            setTextColor(Color.parseColor("#ff5f5f"))
            setPadding(dp(12), dp(6), dp(12), dp(6))
            background = bg(Color.parseColor("#22242d"), 10f)
            setOnClickListener {
                buzz(20, 200)
                ws?.close(1000, null)
                connected = false
                setStatus(false, "已断开")
                mainPage.visibility = View.GONE
                connectPage.visibility = View.VISIBLE
            }
        }
        topbar.addView(statusDot)
        topbar.addView(statusText)
        topbar.addView(exitBtn)

        // 触控板 + 水印
        val padFrame = FrameLayout(this).apply {
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply {
                leftMargin = dp(10); rightMargin = dp(10); topMargin = dp(4)
            }
        }
        padView = TouchpadView(this).apply {
            setBackgroundColor(Color.parseColor("#1b1d24"))
            background = bg(Color.parseColor("#1b1d24"), 18f)
        }
        hintView = TextView(this).apply {
            text = "单指拖动 = 移动指针\n轻点 = 左键 · 双指轻点 = 右键 · 双指滑动 = 滚动"
            textSize = 13.5f
            setTextColor(Color.parseColor("#8b8f9a"))
            gravity = Gravity.CENTER
            alpha = 0.5f
        }
        padFrame.addView(padView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        padFrame.addView(hintView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.CENTER))
        mainPage.addView(topbar)
        mainPage.addView(padFrame)

        // 鼠标键行
        val mouseRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            setPadding(dp(10), dp(6), dp(10), dp(6))
        }
        val mouseBtns = listOf(
            "左键" to "left", "中键" to "middle", "右键" to "right",
            "后退" to "back", "前进" to "forward")
        mouseBtns.forEach { (label, btn) ->
            val b = TextView(this).apply {
                text = label
                textSize = 14f
                gravity = Gravity.CENTER
                setTextColor(Color.WHITE)
                background = bg(Color.parseColor("#22242d"), 12f)
                layoutParams = LinearLayout.LayoutParams(0, dp(44), 1f).apply { rightMargin = dp(7) }
                setOnClickListener {
                    when (btn) {
                        "left" -> buzz(10, 200)
                        "right" -> buzz(24, 220)
                        else -> buzz(16, 200)
                    }
                    sendClick(btn)
                }
            }
            mouseRow.addView(b)
        }
        // 拖拽按钮：按住=左键按下
        val dragBtn = TextView(this).apply {
            text = "拖拽"
            textSize = 14f
            gravity = Gravity.CENTER
            setTextColor(Color.WHITE)
            background = bg(Color.parseColor("#22242d"), 12f)
            layoutParams = LinearLayout.LayoutParams(0, dp(44), 1f)
            @SuppressLint("ClickableViewAccessibility")
            setOnTouchListener { v, ev ->
                when (ev.actionMasked) {
                    MotionEvent.ACTION_DOWN -> {
                        sendDown("left"); buzz(28, 255)
                        v.background = bg(Color.parseColor("#3d7bff"), 12f); true
                    }
                    MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                        sendUp("left")
                        v.background = bg(Color.parseColor("#22242d"), 12f); true
                    }
                    else -> true
                }
            }
        }
        mouseRow.addView(dragBtn)
        mainPage.addView(mouseRow)

        // 键盘输入行
        val kbRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(10), 0, dp(10), dp(8))
        }
        keyboardInput = EditText(this).apply {
            hint = "输入文字，实时打到电脑（缓冲整句发送）"
            textSize = 15f
            setSingleLine()
            setTextColor(Color.WHITE)
            setHintTextColor(Color.parseColor("#6c7079"))
            background = bg(Color.parseColor("#16171d"), 10f)
            layoutParams = LinearLayout.LayoutParams(0, dp(42), 1f).apply { rightMargin = dp(8) }
            setPadding(dp(10), 0, dp(10), 0)
        }
        val enterBtn = TextView(this).apply {
            text = "回车"
            textSize = 13f
            gravity = Gravity.CENTER
            setTextColor(Color.parseColor("#5c8dff"))
            setPadding(dp(10), dp(6), dp(10), dp(6))
            setOnClickListener {
                flushBuffer()
                sendKey("return")
                buzz(10, 180)
            }
        }
        val backBtn = TextView(this).apply {
            text = "退格"
            textSize = 13f
            gravity = Gravity.CENTER
            setTextColor(Color.parseColor("#5c8dff"))
            setPadding(dp(10), dp(6), dp(10), dp(6))
            setOnClickListener {
                if (keyboardInput.text.isEmpty()) sendKey("backspace")
                else keyboardInput.setText("")
                buzz(10, 180)
            }
        }
        kbRow.addView(keyboardInput)
        kbRow.addView(enterBtn)
        kbRow.addView(backBtn)
        mainPage.addView(kbRow)
        root.addView(mainPage)
        setContentView(root)

        // 键盘缓冲清空模型（与网页端一致：整句发送后清空，杜绝镜像跳变）
        keyboardInput.addTextChangedListener(object : TextWatcher {
            override fun afterTextChanged(s: Editable?) {
                handler.removeCallbacks(flushRun)
                if (!s.isNullOrEmpty()) handler.postDelayed(flushRun, 320)
            }
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
        })

        host = getSharedPreferences("padlink", MODE_PRIVATE).getString("host", "") ?: ""
        hostInput.setText(host)
        if (host.isNotEmpty()) connect(host)
    }

    private val flushRun = Runnable {
        val v = keyboardInput.text.toString()
        if (v.isNotEmpty()) {
            sendText(v)
            keyboardInput.setText("")
        }
    }

    // ---- WebSocket ----
    private var pingRunnable: Runnable? = null

    private fun connect(h: String) {
        if (h.isEmpty()) return
        ws?.cancel()
        setStatus(false, "连接中… $h")
        val request = Request.Builder().url("ws://$h:8765/ws").build()
        ws = httpClient.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                runOnUiThread {
                    connected = true
                    host = h
                    getSharedPreferences("padlink", MODE_PRIVATE).edit().putString("host", h).apply()
                    connectPage.visibility = View.GONE
                    mainPage.visibility = View.VISIBLE
                    setStatus(true, "已连接 $h")
                    buzzPattern(longArrayOf(12, 40, 12, 40, 24))
                    sendPing()
                    startPing()
                }
            }
            override fun onMessage(webSocket: WebSocket, text: String) {
                runOnUiThread {
                    try {
                        val m = JSONObject(text)
                        when (m.optString("t")) {
                            "pong" -> setStatus(true, "已连接 $h · ${latencyOf(m)}ms")
                        }
                    } catch (_: Exception) {}
                }
            }
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                runOnUiThread {
                    connected = false
                    stopPing()
                    if (mainPage.visibility == View.VISIBLE) {
                        setStatus(false, "断开，2s 后重连…")
                        handler.postDelayed({ if (!connected) connect(host) }, 2000)
                    } else {
                        setStatus(false, "未连接")
                        android.widget.Toast.makeText(this@MainActivity, "连接失败：检查同一WiFi/服务端/防火墙", android.widget.Toast.LENGTH_LONG).show()
                    }
                }
            }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                runOnUiThread { connected = false; stopPing() }
            }
        })
    }

    private fun latencyOf(m: JSONObject): Long {
        val ts = m.optDouble("ts", 0.0)
        return maxOf(0, (System.currentTimeMillis() - ts).toLong())
    }

    private fun setStatus(on: Boolean, text: String) {
        statusDot.setTextColor(if (on) Color.parseColor("#34c77b") else Color.parseColor("#e05555"))
        statusText.text = text
    }

    private fun startPing() {
        stopPing()
        val r = object : Runnable {
            override fun run() {
                sendPing()
                handler.postDelayed(this, 2000)
            }
        }
        pingRunnable = r
        handler.post(r)
    }
    private fun stopPing() { pingRunnable?.let { handler.removeCallbacks(it) }; pingRunnable = null }

    private fun sendPing() {
        try { ws?.send(JSONObject().put("t", "ping").put("ts", System.currentTimeMillis()).toString()) } catch (_: Exception) {}
    }

    private fun send(o: JSONObject) {
        if (connected) try { ws?.send(o.toString()) } catch (_: Exception) {}
    }
    private fun sendMove(dx: Double, dy: Double) = send(JSONObject().put("t", "move").put("dx", dx).put("dy", dy))
    private fun sendClick(btn: String) = send(JSONObject().put("t", "click").put("btn", btn))
    private fun sendDown(btn: String) = send(JSONObject().put("t", "down").put("btn", btn))
    private fun sendUp(btn: String) = send(JSONObject().put("t", "up").put("btn", btn))
    private fun sendScroll(dy: Double) = send(JSONObject().put("t", "scroll").put("dx", 0).put("dy", dy))
    private fun sendText(v: String) = send(JSONObject().put("t", "text").put("value", v))
    private fun sendKey(name: String) = send(JSONObject().put("t", "key").put("value", name))
    private fun sendKey(name: String, count: Int) = send(JSONObject().put("t", "key").put("value", name).put("count", count))

    private fun flushBuffer() {
        val v = (hostInput.text.toString())
        if (v.isNotEmpty()) { sendText(v); hostInput.setText("") }
    }

    private fun buzzPattern(pattern: LongArray) {
        if (Build.VERSION.SDK_INT >= 26) {
            val amps = pattern.map { if (it == 0L) 0 else 200 }.toIntArray()
            vibrator.vibrate(VibrationEffect.createWaveform(longArrayOf(0) + pattern, intArrayOf(0) + amps, -1))
        } else buzz(pattern.sum())
    }

    // ---- 触控板手势视图 ----
    private class PF(var x: Float, var y: Float, var sx: Float, var sy: Float, var st: Long, var moved: Boolean)

    private inner class TouchpadView(context: Context) : View(context) {
        private val pointers = HashMap<Int, PF>()
        private var padLast: Pair<Float, Float>? = null
        private var scrollRef: Int? = null
        private var lastScrollY = 0f
        private var scrollAccum = 0f
        private val tapMs = 260L
        private val tapDist = 10f

        @SuppressLint("ClickableViewAccessibility")
        override fun onTouchEvent(e: MotionEvent): Boolean {
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                    for (i in 0 until e.pointerCount) {
                        val id = e.getPointerId(i)
                        if (!pointers.containsKey(id))
                            pointers[id] = PF(e.getX(i), e.getY(i), e.getX(i), e.getY(i), System.currentTimeMillis(), false)
                    }
                    val ids = pointers.keys.toList()
                    if (ids.size == 1) {
                        padLast = e.getX(0) to e.getY(0)
                    } else if (ids.size >= 2) {
                        padLast = null
                        scrollRef = ids[0]
                        lastScrollY = pointers[ids[0]]!!.y
                        scrollAccum = 0f
                    }
                }
                MotionEvent.ACTION_MOVE -> {
                    val ids = pointers.keys.toList()
                    if (ids.size == 1) {
                        val id = ids[0]
                        val i = e.findPointerIndex(id)
                        if (i >= 0) {
                            val p = pointers[id]!!
                            val x = e.getX(i); val y = e.getY(i)
                            if (abs(x - p.sx) > tapDist || abs(y - p.sy) > tapDist) p.moved = true
                            padLast?.let { (lx, ly) ->
                                val dx = (x - lx) * sens
                                val dy = (y - ly) * sens
                                if (abs(dx) > 0.4 || abs(dy) > 0.4) sendMove(dx.toDouble(), dy.toDouble())
                            }
                            padLast = x to y
                        }
                    } else if (ids.size >= 2) {
                        val id = scrollRef
                        if (id != null) {
                            val i = e.findPointerIndex(id)
                            if (i >= 0) {
                                val p = pointers[id]!!
                                p.moved = true
                                val y = e.getY(i)
                                val dy = (y - p.y) * scrollSpeed
                                if (abs(dy) > 0.4) sendScroll(dy.toDouble())
                                scrollAccum += abs(dy)
                                if (scrollAccum > 30) { tickVib(); scrollAccum = 0f }
                                p.y = y
                            }
                        }
                    }
                }
                MotionEvent.ACTION_POINTER_UP -> {
                    val id = e.getPointerId(e.actionIndex)
                    val wasTwo = pointers.size == 2
                    val p = pointers[id]
                    pointers.remove(id)
                    if (wasTwo && p != null) {
                        val allTaps = e.pointerCount <= 2
                        if (allTaps && !p.moved && System.currentTimeMillis() - p.st < tapMs) {
                            sendClick("right"); buzz(24, 220)
                        }
                        scrollRef = null
                    }
                }
                MotionEvent.ACTION_UP -> {
                    if (pointers.size == 1) {
                        val p = pointers.values.first()
                        if (!p.moved && System.currentTimeMillis() - p.st < tapMs) {
                            sendClick("left"); buzz(10, 220)
                        }
                    }
                    pointers.clear()
                    padLast = null
                    scrollRef = null
                    showHintBack()
                }
                MotionEvent.ACTION_CANCEL -> {
                    pointers.clear()
                    padLast = null
                    scrollRef = null
                    showHintBack()
                }
            }
            return true
        }
    }

    private fun showHintBack() {
        handler.postDelayed({ hintView.alpha = 0.5f }, 4000)
    }
}
