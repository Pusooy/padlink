package com.padlink.app

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
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
import android.text.InputType
import android.text.TextUtils
import android.text.TextWatcher
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import okhttp3.Call
import okhttp3.Callback
import com.padlink.app.R
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import kotlin.math.abs
import kotlin.math.max

/**
 * PadLink 安卓端 —— 交互参考 Remote Mouse：
 * 全屏触控板为主体，底部 Dock 五页签（触控板 / 键盘 / 媒体 / 应用 / 设置）。
 * 震动通过 VibrationEffect 做振幅级精细调制（仿生手感）。
 */
class MainActivity : android.app.Activity() {

    private val C_BG = Color.parseColor("#0a0b0f")
    private val C_SURFACE = Color.parseColor("#13151b")
    private val C_SURFACE2 = Color.parseColor("#1c1f28")
    private val C_PAD = Color.parseColor("#0f1117")
    private val C_EDGE = Color.parseColor("#242836")
    private val C_TEXT = Color.parseColor("#eceef4")
    private val C_TEXT2 = Color.parseColor("#9aa3b2")
    private val C_TEXT3 = Color.parseColor("#626a78")
    private val C_ACCENT = Color.parseColor("#5b8cff")
    private val C_ACCENT_SOFT = Color.parseColor("#5b8cff30")
    private val C_OK = Color.parseColor("#3ddc97")
    private val C_DANGER = Color.parseColor("#ff6b6b")

    private lateinit var vibrator: Vibrator
    private val handler = Handler(Looper.getMainLooper())

    /** OkHttp 默认不开 TCP_NODELAY，输入小包会被 Nagle 算法攒住（最坏 +40ms）。
     *  这里对所有出站套接字强制关闭 Nagle——蓝牙级延迟的关键一步。 */
    private class NoDelaySocketFactory : javax.net.SocketFactory() {
        private val def = javax.net.SocketFactory.getDefault()
        private fun fix(s: java.net.Socket): java.net.Socket =
            s.apply { try { tcpNoDelay = true } catch (_: Exception) {} }
        override fun createSocket(): java.net.Socket = fix(def.createSocket())
        override fun createSocket(host: String?, port: Int): java.net.Socket = fix(def.createSocket(host, port))
        override fun createSocket(host: String?, port: Int, localHost: java.net.InetAddress?, localPort: Int): java.net.Socket =
            fix(def.createSocket(host, port, localHost, localPort))
        override fun createSocket(host: java.net.InetAddress?, port: Int): java.net.Socket = fix(def.createSocket(host, port))
        override fun createSocket(address: java.net.InetAddress?, port: Int, localAddress: java.net.InetAddress?, localPort: Int): java.net.Socket =
            fix(def.createSocket(address, port, localAddress, localPort))
    }

    private val http = OkHttpClient.Builder()
        .socketFactory(NoDelaySocketFactory())
        .build()

    private var ws: WebSocket? = null
    private var connected = false
    private var host = ""
    private var pingJob: Runnable? = null

    private var sens = 1.6f
    private var scrollSpd = 2f
    private var vibOn = true
    private var vibStrength = "normal"
    private var hintOn = true

    private lateinit var connectPage: LinearLayout
    private lateinit var appRoot: LinearLayout
    private lateinit var statusDot: TextView
    private lateinit var statusText: TextView
    private lateinit var pageHost: FrameLayout
    private lateinit var hostInput: EditText
    private lateinit var scanStatus: TextView
    private lateinit var hintView: TextView
    private lateinit var padView: TouchpadView
    private lateinit var volSeek: SeekBar
    private lateinit var volNum: TextView
    private lateinit var winListBox: LinearLayout
    private lateinit var appGrid: LinearLayout
    private lateinit var appSearch: EditText

    private var dockButtons: List<LinearLayout> = emptyList()
    private val pages = LinkedHashMap<String, View>()
    private val pageKeys = listOf("pad", "kb", "media", "apps", "screen", "files", "settings")

    private val iconCache = HashMap<String, Bitmap>()
    private var allPrograms: List<JSONObject> = emptyList()

    // ============ 生命周期 ============
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Android 16/17 对窗口 API 更严格：先设置主题再建视图，避免 Theme 应用失败导致白屏
        try { setTheme(android.R.style.Theme_DeviceDefault_NoActionBar) } catch (_: Throwable) { }

        // 兜底：记录完整堆栈（可通过 adb logcat -s PadLink 查看）
        Thread.setDefaultUncaughtExceptionHandler { thread, ex ->
            android.util.Log.e("PadLink", "未捕获异常 @ ${thread.name}", ex)
            try { showFatalError(ex) } catch (_: Throwable) { }
        }

        vibrator = try {
            if (Build.VERSION.SDK_INT >= 31) {
                val vm = getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager
                vm?.defaultVibrator ?: @Suppress("DEPRECATION") (getSystemService(VIBRATOR_SERVICE) as Vibrator)
            } else {
                @Suppress("DEPRECATION") (getSystemService(VIBRATOR_SERVICE) as Vibrator)
            }
        } catch (e: Throwable) {
            @Suppress("DEPRECATION") (getSystemService(VIBRATOR_SERVICE) as Vibrator)
        }

        // 方向由 AndroidManifest 的 screenOrientation 声明，运行时不再设置
        // （部分 ROM 在 API 36+ 上对运行时设置方向会抛异常）
        loadPrefs()
        try {
            buildUi()
        } catch (fatal: Throwable) {
            android.util.Log.e("PadLink", "buildUi 失败", fatal)
            showFatalError(fatal)
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
    }

    override fun onDestroy() {
        pingJob?.let { handler.removeCallbacks(it) }
        try { ws?.close(1000, null) } catch (_: Exception) {}
        super.onDestroy()
    }

    override fun onPause() {
        releaseKeyboard()
        sendReleaseAll()
        super.onPause()
    }

    /** Android 15+ 强制 edge-to-edge（targetSdk 35）：把系统栏区域作为内边距传给根布局。 */
    private fun setupEdgeToEdge() {
        try {
            if (Build.VERSION.SDK_INT >= 35) {
                window.setDecorFitsSystemWindows(false)
            }
        } catch (_: Throwable) { }
    }

    /**
     * 最后的兜底：任何 UI 构建失败都以可读文本展示，绝不让进程闪退。
     * 用户可长按复制堆栈反馈。
     */
    private fun showFatalError(t: Throwable) {
        try {
            val detail = buildString {
                appendLine("PadLink 启动失败")
                appendLine("设备: ${Build.MANUFACTURER} ${Build.MODEL}")
                appendLine("Android: ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
                appendLine()
                appendLine(t.toString())
                appendLine()
                t.stackTrace.take(24).forEach { appendLine(it.toString()) }
            }
            val tv = TextView(this).apply {
                text = detail
                textSize = 11f
                setTextColor(android.graphics.Color.WHITE)
                setBackgroundColor(android.graphics.Color.parseColor("#0d0e12"))
                setPadding(32, 32, 32, 32)
                typeface = Typeface.MONOSPACE
                setTextIsSelectable(true)
            }
            val sv = ScrollView(this).apply { addView(tv) }
            setContentView(sv)
        } catch (_: Throwable) {
            // 连错误界面都建不出来，只能静默
        }
    }

    private fun hideSystemBars() { /* 已禁用 */ }

    // ============ 仿生震动 ============
    private fun buzz(kind: String) {
        if (!vibOn) return
        val k = when (vibStrength) { "light" -> 0.55f; "strong" -> 1.55f; else -> 1f }
        val amp = when (vibStrength) { "light" -> 120; "strong" -> 255; else -> 190 }
        val base: LongArray = when (kind) {
            "tap" -> longArrayOf(0, 9)
            "left" -> longArrayOf(0, 14)
            "right" -> longArrayOf(0, 9, 28, 9)
            "middle" -> longArrayOf(0, 7, 20, 7, 20, 7)
            "wheel" -> longArrayOf(0, 5)
            "page" -> longArrayOf(0, 10, 30, 13)
            "heavy" -> longArrayOf(0, 24)
            else -> longArrayOf(0, 9)
        }
        val scaled = LongArray(base.size) { i ->
            val v = base[i]
            if (i == 0 || v == 0L) v else max(2L, (v * k).toLong())
        }
        try {
            if (Build.VERSION.SDK_INT >= 26) {
                val amps = IntArray(scaled.size) { i -> if (i == 0 || scaled[i] == 0L) 0 else amp }
                vibrator.vibrate(VibrationEffect.createWaveform(scaled, amps, -1))
            } else {
                vibrator.vibrate(scaled.sum())
            }
        } catch (_: Exception) {}
    }

    // ============ 偏好 ============
    private val prefs by lazy { getSharedPreferences("padlink", MODE_PRIVATE) }

    private fun loadPrefs() {
        host = prefs.getString("host", "") ?: ""
        sens = prefs.getFloat("sens", 1.6f)
        scrollSpd = prefs.getFloat("scroll", 2f)
        vibOn = prefs.getBoolean("vib", true)
        vibStrength = prefs.getString("vibStrength", "normal") ?: "normal"
        hintOn = prefs.getBoolean("hint", true)
    }

    private fun saveStr(k: String, v: String) = prefs.edit().putString(k, v).apply()
    private fun saveBool(k: String, v: Boolean) = prefs.edit().putBoolean(k, v).apply()
    private fun saveFloat(k: String, v: Float) = prefs.edit().putFloat(k, v).apply()

    // ============ UI 工具 ============
    private fun dp(v: Number): Int = (v.toFloat() * resources.displayMetrics.density).toInt()

    private fun bg(color: Int, radiusDp: Float = 0f): GradientDrawable =
        GradientDrawable().apply {
            setColor(color)
            if (radiusDp > 0f) cornerRadius = radiusDp * resources.displayMetrics.density
        }

    private fun strokeBg(color: Int, stroke: Int, radiusDp: Float): GradientDrawable =
        GradientDrawable().apply {
            setColor(color)
            setStroke(dp(1), stroke)
            cornerRadius = radiusDp * resources.displayMetrics.density
        }

    private fun tv(text: String, size: Float, color: Int, bold: Boolean = false): TextView =
        TextView(this).apply {
            this.text = text
            textSize = size
            setTextColor(color)
            if (bold) typeface = Typeface.DEFAULT_BOLD
        }

    private fun card(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = strokeBg(C_SURFACE, C_EDGE, 16f)
        setPadding(dp(14), dp(13), dp(14), dp(13))
    }

    private fun sectionTitle(text: String): TextView = tv(text, 11.5f, C_TEXT3, true).apply {
        letterSpacing = 0.06f
        setPadding(dp(2), dp(9), dp(2), dp(7))
    }

    // ============ 主界面 ============
    private fun buildUi() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(C_BG)
        }

        // --- 连接页 ---
        connectPage = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(C_BG)
        }
        connectPage.addView(tv("◉", 34f, Color.WHITE).apply {
            gravity = Gravity.CENTER
            background = bg(C_ACCENT, 20f)
        }, LinearLayout.LayoutParams(dp(62), dp(62)))
        connectPage.addView(tv("PadLink", 25f, C_TEXT, true).apply {
            gravity = Gravity.CENTER
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(12) })
        connectPage.addView(tv("手机 → 电脑的触控板 · 键盘 · 遥控器\n请先在电脑上运行 PadLink", 13f, C_TEXT2).apply {
            gravity = Gravity.CENTER
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8); bottomMargin = dp(18) })

        hostInput = EditText(this).apply {
            hint = "电脑 IP，如 192.168.1.100"
            textSize = 16f
            gravity = Gravity.CENTER
            setSingleLine()
            inputType = InputType.TYPE_CLASS_TEXT
            setTextColor(C_TEXT)
            setHintTextColor(C_TEXT3)
            background = strokeBg(C_SURFACE, C_EDGE, 13f)
            setPadding(dp(14), dp(13), dp(14), dp(13))
            if (host.isNotEmpty()) setText(host)
        }
        connectPage.addView(hostInput, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { marginStart = dp(30); marginEnd = dp(30) })

        val connectBtn = tv("连接到电脑", 16f, Color.WHITE, true).apply {
            gravity = Gravity.CENTER
            background = bg(C_ACCENT, 13f)
            setPadding(0, dp(14), 0, dp(14))
            setOnClickListener {
                buzz("page")
                connect(hostInput.text.toString().trim())
            }
        }
        connectPage.addView(connectBtn, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(14); marginStart = dp(30); marginEnd = dp(30) })

        // 局域网扫描
        val scanBtn = tv("◎  扫描局域网", 13f, C_ACCENT).apply {
            gravity = Gravity.CENTER
            setPadding(0, dp(10), 0, dp(8))
            setOnClickListener {
                scanStatus.text = "扫描中…"
                Thread {
                    try {
                        val socket = java.net.DatagramSocket()
                        socket.soTimeout = 5000
                        socket.broadcast = true
                        val data = "PADLINK_DISCOVER".toByteArray()
                        val addr = java.net.InetAddress.getByName("255.255.255.255")
                        socket.send(java.net.DatagramPacket(data, data.size, addr, 8766))
                        val buf = ByteArray(256)
                        val resp = java.net.DatagramPacket(buf, buf.size)
                        socket.receive(resp)
                        val json = JSONObject(String(buf, 0, resp.length))
                        val ip = json.optString("ip", "")
                        socket.close()
                        runOnUiThread {
                            if (ip.isNotEmpty()) {
                                scanStatus.text = "找到: $ip"
                                hostInput.setText(ip)
                                connect(ip)
                            } else scanStatus.text = "未发现设备"
                        }
                    } catch (e: Exception) {
                        runOnUiThread { scanStatus.text = "未发现，请手动输入 IP" }
                    }
                }.start()
            }
        }
        connectPage.addView(scanBtn, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            gravity = Gravity.CENTER
        })
        scanStatus = tv("", 12f, C_TEXT3).apply {
            gravity = Gravity.CENTER
        }
        connectPage.addView(scanStatus, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            gravity = Gravity.CENTER
        })

        root.addView(connectPage, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        // --- 主体 ---
        appRoot = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
        }
        val statusbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(13), dp(6), dp(13), dp(2))
        }
        statusDot = tv("●", 9f, C_DANGER)
        statusText = tv("未连接", 11.5f, C_TEXT3).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            setPadding(dp(6), 0, 0, 0)
        }
        statusbar.addView(statusDot)
        statusbar.addView(statusText)
        appRoot.addView(statusbar, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        pageHost = FrameLayout(this).apply { setPadding(dp(10), dp(2), dp(10), dp(8)) }
        appRoot.addView(pageHost, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))

        pages["pad"] = buildPadPage()
        pages["kb"] = buildKeyboardPage()
        pages["media"] = buildMediaPage()
        pages["apps"] = buildAppsPage()
        pages["screen"] = buildScreenPage()
        pages["files"] = buildFilesPage()
        pages["settings"] = buildSettingsPage()
        pages.forEach { (_, v) ->
            pageHost.addView(v, FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        }

        // Dock（Material 矢量图标 + Fluent 间距）
        val dock = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setBackgroundColor(C_SURFACE)
            setPadding(dp(4), dp(6), dp(4), dp(6))
        }
        val dockItems = listOf(
            Triple("pad", "触控板", R.drawable.ic_touchpad),
            Triple("kb", "键盘", R.drawable.ic_keyboard),
            Triple("media", "媒体", R.drawable.ic_media),
            Triple("apps", "应用", R.drawable.ic_apps),
            Triple("screen", "屏幕", R.drawable.ic_screen),
            Triple("files", "文件", R.drawable.ic_files),
            Triple("settings", "设置", R.drawable.ic_settings),
        )
        dockButtons = dockItems.map { (key, label, iconRes) ->
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                setPadding(dp(2), dp(7), dp(2), dp(7))
                layoutParams = LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                // 矢量图标
                val iv = ImageView(this@MainActivity).apply {
                    setImageResource(iconRes)
                    layoutParams = LinearLayout.LayoutParams(dp(22), dp(22))
                    imageTintList = android.content.res.ColorStateList.valueOf(C_TEXT3)
                }
                addView(iv)
                // 标签
                addView(tv(label, 10f, C_TEXT3).apply { setPadding(0, dp(3), 0, 0) })
                setOnClickListener { selectDock(key); buzz("page") }
            }
        }
        dockButtons.forEach { dock.addView(it) }
        appRoot.addView(dock, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        root.addView(appRoot, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        setContentView(root)

        selectDockQuiet("pad")
        showPage("pad")

        if (host.isNotEmpty()) connect(host)
    }

    private var currentPage = "pad"

    private fun showPage(key: String) {
        currentPage = key
        pages.forEach { (k, v) -> v.visibility = if (k == key) View.VISIBLE else View.GONE }
    }

    private fun selectDockQuiet(key: String) {
        dockButtons.forEachIndexed { i, b ->
            val on = pageKeys[i] == key
            val iv = b.getChildAt(0) as ImageView
            val lb = b.getChildAt(1) as TextView
            val color = if (on) C_ACCENT else C_TEXT3
            iv.imageTintList = android.content.res.ColorStateList.valueOf(color)
            lb.setTextColor(color)
            b.background = if (on) bg(Color.parseColor("#5b8cff26"), 14f) else null
        }
    }

    private fun selectDock(key: String) {
        if (currentPage == "kb" && key != "kb") releaseKeyboard()
        showPage(key)
        selectDockQuiet(key)
        buzz("page")
        when (key) {
            "media" -> {
                sendJson(JSONObject().put("t", "get_volume"))
                sendJson(JSONObject().put("t", "get_sysinfo"))
            }
            "apps" -> { loadWindows(); loadPrograms() }
            "pad" -> hintVisible(true)
            else -> Unit
        }
    }

    // ============ 触控板页 ============
    private fun buildPadPage(): View {
        val frame = FrameLayout(this)
        padView = TouchpadView(this).apply { background = strokeBg(C_PAD, C_EDGE, 16f) }
        frame.addView(padView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        hintView = tv("单指拖动 · 移动指针\n轻点左键 · 双指轻点右键\n双指上下滑动 · 滚动", 13f, C_TEXT3).apply {
            gravity = Gravity.CENTER
            setLineSpacing(dp(4).toFloat(), 1f)
        }
        frame.addView(hintView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.CENTER))
        hintView.visibility = if (hintOn) View.VISIBLE else View.GONE

        val quick = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.TOP or Gravity.END).apply {
                topMargin = dp(10); rightMargin = dp(10)
            }
        }
        listOf("左" to 0, "中" to 2, "右" to 1).forEach { (label, btn) ->
            val b = tv(label, 13f, C_TEXT2, true).apply {
                gravity = Gravity.CENTER
                background = strokeBg(C_SURFACE, C_EDGE, 14f)
                setOnClickListener {
                    sendBtn(btn, true); sendBtn(btn, false)
                    buzz(if (btn == 1) "right" else if (btn == 2) "middle" else "left")
                }
            }
            quick.addView(b, LinearLayout.LayoutParams(dp(44), dp(44)).apply {
                bottomMargin = dp(7)
            })
        }
        frame.addView(quick)
        return frame
    }

    private fun hintVisible(show: Boolean) {
        if (!this::hintView.isInitialized) return
        if (!hintOn) { hintView.visibility = View.GONE; return }
        hintView.visibility = View.VISIBLE
        hintView.animate().alpha(if (show) 1f else 0f).setDuration(300).start()
    }

    // ============ 键盘页 ============
    // 蓝牙键盘语义：每个键按下/抬起各发一条 VK 事件直达 PC，
    // 拼音组字完全交给电脑自己的输入法。手机端没有任何输入缓冲或文本框。
    private data class KeySpec(
        val label: String,
        val shiftLabel: String? = null,
        val vk: Int,
        val weight: Float = 1f,
        val kind: Int = 0,          // 0 普通 / 1 修饰键 / 2 锁定键 / 3 连发键
    )

    private val kbRows: List<List<KeySpec>> = listOf(
        listOf(
            KeySpec("esc", vk = 0x1B, weight = 1.3f),
            KeySpec("F1", vk = 0x70), KeySpec("F2", vk = 0x71), KeySpec("F3", vk = 0x72),
            KeySpec("F4", vk = 0x73), KeySpec("F5", vk = 0x74), KeySpec("F6", vk = 0x75),
            KeySpec("F7", vk = 0x76), KeySpec("F8", vk = 0x77), KeySpec("F9", vk = 0x78),
            KeySpec("F10", vk = 0x79), KeySpec("F11", vk = 0x7A), KeySpec("F12", vk = 0x7B),
            KeySpec("del", vk = 0x2E, weight = 1.3f, kind = 3)),
        listOf(
            KeySpec("`", "~", 0xC0), KeySpec("1", "!", 0x31), KeySpec("2", "@", 0x32),
            KeySpec("3", "#", 0x33), KeySpec("4", "$", 0x34), KeySpec("5", "%", 0x35),
            KeySpec("6", "^", 0x36), KeySpec("7", "&", 0x37), KeySpec("8", "*", 0x38),
            KeySpec("9", "(", 0x39), KeySpec("0", ")", 0x30), KeySpec("-", "_", 0xBD),
            KeySpec("=", "+", 0xBB), KeySpec("⌫", vk = 0x08, weight = 1.9f, kind = 3)),
        listOf(
            KeySpec("tab", vk = 0x09, weight = 1.5f),
            KeySpec("q", vk = 0x51), KeySpec("w", vk = 0x57), KeySpec("e", vk = 0x45),
            KeySpec("r", vk = 0x52), KeySpec("t", vk = 0x54), KeySpec("y", vk = 0x59),
            KeySpec("u", vk = 0x55), KeySpec("i", vk = 0x49), KeySpec("o", vk = 0x4F),
            KeySpec("p", vk = 0x50), KeySpec("[", "{", 0xDB), KeySpec("]", "}", 0xDD),
            KeySpec("\\", "|", 0xDC)),
        listOf(
            KeySpec("caps", vk = 0x14, weight = 1.8f, kind = 2),
            KeySpec("a", vk = 0x41), KeySpec("s", vk = 0x53), KeySpec("d", vk = 0x44),
            KeySpec("f", vk = 0x46), KeySpec("g", vk = 0x47), KeySpec("h", vk = 0x48),
            KeySpec("j", vk = 0x4A), KeySpec("k", vk = 0x4B), KeySpec("l", vk = 0x4C),
            KeySpec(";", ":", 0xBA), KeySpec("'", "\"", 0xDE),
            KeySpec("⏎", vk = 0x0D, weight = 1.8f, kind = 3)),
        listOf(
            KeySpec("⇧", vk = 0xA0, weight = 1.9f, kind = 1),
            KeySpec("z", vk = 0x5A), KeySpec("x", vk = 0x58), KeySpec("c", vk = 0x43),
            KeySpec("v", vk = 0x56), KeySpec("b", vk = 0x42), KeySpec("n", vk = 0x4E),
            KeySpec("m", vk = 0x4D), KeySpec(",", "<", 0xBC), KeySpec(".", ">", 0xBE),
            KeySpec("/", "?", 0xBF), KeySpec("⇧", vk = 0xA0, weight = 1.9f, kind = 1)),
        listOf(
            KeySpec("ctrl", vk = 0xA2, weight = 1.4f, kind = 1),
            KeySpec("win", vk = 0x5B, weight = 1.2f, kind = 1),
            KeySpec("alt", vk = 0xA4, weight = 1.2f, kind = 1),
            KeySpec("", vk = 0x20, weight = 4.6f, kind = 3),
            KeySpec("←", vk = 0x25, weight = 0.8f, kind = 3),
            KeySpec("↑", vk = 0x26, weight = 0.8f, kind = 3),
            KeySpec("↓", vk = 0x28, weight = 0.8f, kind = 3),
            KeySpec("→", vk = 0x27, weight = 0.8f, kind = 3)),
    )

    private val pressedKeys = HashSet<Int>()     // 手指正按着的 VK
    private val latchedMods = HashSet<Int>()     // 粘滞中的修饰键（保持按下）
    private val modDownAt = HashMap<Int, Long>()
    private val modTouchedOther = HashSet<Int>() // 按住期间敲过别的键 → 抬起时不粘滞
    private val keyRefs = mutableListOf<Pair<TextView, KeySpec>>()
    private var capsAssumed = false
    private var repeatVk = 0

    private val repeatRun = object : Runnable {
        override fun run() {
            if (repeatVk == 0) return
            sendKeyVk(repeatVk, true, repeat = true)
            handler.postDelayed(this, 35)
        }
    }
    private val repeatStart = Runnable {
        if (repeatVk != 0) {
            sendKeyVk(repeatVk, true, repeat = true)
            handler.postDelayed(repeatRun, 35)
        }
    }

    private fun shiftActive() = (0xA0 in pressedKeys) || (0xA0 in latchedMods)

    private fun buildKeyboardPage(): View {
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(2), 0, 0)
        }
        val grid = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(grid, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        keyRefs.clear()
        kbRows.forEach { row ->
            val rowView = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                layoutParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply { topMargin = dp(3) }
            }
            row.forEach { spec ->
                val key = TextView(this).apply {
                    textSize = if (spec.label.length == 1) 15f else 10.5f
                    typeface = Typeface.DEFAULT_BOLD
                    gravity = Gravity.CENTER
                    setTextColor(C_TEXT)
                    background = bg(C_SURFACE, 9f)
                    layoutParams = LinearLayout.LayoutParams(0,
                        ViewGroup.LayoutParams.MATCH_PARENT, spec.weight).apply { marginEnd = dp(3) }
                    setOnTouchListener { v, ev ->
                        when (ev.actionMasked) {
                            MotionEvent.ACTION_DOWN -> { v.isPressed = true; onKeyPress(spec); true }
                            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                                v.isPressed = false; onKeyRelease(spec); true }
                            else -> false
                        }
                    }
                }
                keyRefs.add(key to spec)
                rowView.addView(key)
            }
            grid.addView(rowView)
        }
        refreshKeyStyles()
        return col
    }

    private fun onKeyPress(spec: KeySpec) {
        buzz("tap")
        when (spec.kind) {
            1 -> {  // 修饰键：按下发 down，抬起时决定是否粘滞
                if (spec.vk in latchedMods) return
                pressedKeys.add(spec.vk)
                modDownAt[spec.vk] = System.currentTimeMillis()
                modTouchedOther.remove(spec.vk)
                sendKeyVk(spec.vk, true)
            }
            2 -> {  // CapsLock：点按 down+up，本地指示灯
                sendKeyVk(spec.vk, true)
                sendKeyVk(spec.vk, false)
                capsAssumed = !capsAssumed
            }
            else -> {
                // 需要 Shift 的符号：像真键盘一样先包一层 Shift
                if (spec.shiftLabel != null && !shiftActive()) sendKeyVk(0xA0, true)
                pressedKeys.add(spec.vk)
                sendKeyVk(spec.vk, true)
                if (spec.kind == 3) {
                    repeatVk = spec.vk
                    handler.postDelayed(repeatStart, 400)
                }
                // 按住的修饰键标记「被使用」，抬起时不再粘滞
                pressedKeys.forEach { if (it != spec.vk) modTouchedOther.add(it) }
            }
        }
        refreshKeyStyles()
    }

    private fun onKeyRelease(spec: KeySpec) {
        when (spec.kind) {
            1 -> {
                pressedKeys.remove(spec.vk)
                val quick = System.currentTimeMillis() - (modDownAt[spec.vk] ?: 0L) < 220
                when {
                    spec.vk in latchedMods -> { latchedMods.remove(spec.vk); sendKeyVk(spec.vk, false) }
                    quick && spec.vk !in modTouchedOther -> latchedMods.add(spec.vk)  // 粘滞保持
                    else -> sendKeyVk(spec.vk, false)
                }
                modDownAt.remove(spec.vk)
            }
            else -> {
                handler.removeCallbacks(repeatStart)
                handler.removeCallbacks(repeatRun)
                repeatVk = 0
                pressedKeys.remove(spec.vk)
                sendKeyVk(spec.vk, false)
                if (spec.shiftLabel != null && !shiftActive()) sendKeyVk(0xA0, false)
                releaseLatchedMods()
            }
        }
        refreshKeyStyles()
    }

    private fun releaseLatchedMods() {
        if (latchedMods.isEmpty()) return
        latchedMods.forEach { sendKeyVk(it, false) }
        latchedMods.clear()
    }

    /** 离开键盘页 / 退到后台：抬起所有键，杜绝「Shift 卡住」这类幽灵状态。 */
    private fun releaseKeyboard() {
        handler.removeCallbacks(repeatStart)
        handler.removeCallbacks(repeatRun)
        repeatVk = 0
        if (pressedKeys.isNotEmpty() || latchedMods.isNotEmpty()) {
            sendReleaseAll()
            pressedKeys.clear(); latchedMods.clear(); modDownAt.clear(); modTouchedOther.clear()
            refreshKeyStyles()
        }
    }

    private fun refreshKeyStyles() {
        if (keyRefs.isEmpty()) return
        val shift = shiftActive()
        keyRefs.forEach { (view, spec) ->
            val pressed = spec.vk in pressedKeys
            val on = (spec.vk in latchedMods) || (spec.kind == 2 && capsAssumed)
            view.background = when {
                on -> strokeBg(C_ACCENT_SOFT, C_ACCENT, 9f)
                pressed -> strokeBg(C_SURFACE2, C_ACCENT, 9f)
                else -> bg(C_SURFACE, 9f)
            }
            view.setTextColor(if (on || pressed) C_ACCENT else C_TEXT)
            view.text = when {
                spec.shiftLabel != null && shift -> spec.shiftLabel
                spec.vk in 0x41..0x5A && (shift != capsAssumed) -> spec.vk.toChar().toString()
                else -> spec.label
            }
        }
    }

    // ============ 媒体页 ============
    private fun buildMediaPage(): View {
        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(col)

        val play = card()
        play.addView(sectionTitle("播放"))
        val transport = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
        }
        listOf("⏮" to "media_previous", "▶" to "media_play_pause",
               "⏭" to "media_next", "■" to "media_stop").forEachIndexed { i, pair ->
            val primary = i == 1
            val b = tv(pair.first, if (primary) 25f else 20f,
                if (primary) Color.WHITE else C_TEXT).apply {
                gravity = Gravity.CENTER
                background = bg(if (primary) C_ACCENT else C_SURFACE2, if (primary) 19f else 15f)
                setOnClickListener { sendKey(pair.second); buzz("left") }
            }
            transport.addView(b, LinearLayout.LayoutParams(
                dp(if (primary) 60 else 50), dp(if (primary) 60 else 50)).apply {
                marginEnd = dp(10)
            })
        }
        play.addView(transport)
        col.addView(play, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        val vol = card()
        vol.addView(sectionTitle("主机音量"))
        val volRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        volNum = tv("--", 13f, C_TEXT2).apply {
            gravity = Gravity.CENTER
        }
        volRow.addView(volNum, LinearLayout.LayoutParams(dp(46),
            ViewGroup.LayoutParams.WRAP_CONTENT))
        volSeek = SeekBar(this).apply { max = 100 }
        volSeek.layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        volSeek.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(sb: SeekBar?, progress: Int, fromUser: Boolean) {
                if (!fromUser) return
                volNum.text = "$progress%"
                sendJson(JSONObject().put("t", "set_volume").put("level", progress))
            }
            override fun onStartTrackingTouch(sb: SeekBar?) {}
            override fun onStopTrackingTouch(sb: SeekBar?) { buzz("wheel") }
        })
        volRow.addView(volSeek)
        val mute = tv("🔇", 16f, C_TEXT2).apply {
            gravity = Gravity.CENTER
            background = bg(C_SURFACE2, 11f)
            setOnClickListener {
                sendJson(JSONObject().put("t", "toggle_mute"))
                buzz("middle")
                handler.postDelayed({ sendJson(JSONObject().put("t", "get_volume")) }, 300)
            }
        }
        volRow.addView(mute, LinearLayout.LayoutParams(dp(42), dp(42)).apply {
            marginStart = dp(8)
        })
        vol.addView(volRow)
        col.addView(vol, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(10)
        })

        val sys = card()
        sys.addView(sectionTitle("系统"))
        val chips = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        listOf("显示桌面" to "win+d", "任务视图" to "win+tab", "锁定" to "win+l").forEach { pair ->
            val c = tv(pair.first, 12.5f, C_TEXT).apply {
                gravity = Gravity.CENTER
                background = bg(C_SURFACE2, 20f)
                setPadding(dp(12), dp(8), dp(12), dp(8))
                setOnClickListener { sendKey(pair.second); buzz("left") }
            }
            chips.addView(c, LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { marginEnd = dp(8) })
        }
        sys.addView(chips)
        col.addView(sys, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })

        // 剪贴板
        val clipboard = ExtraPages.clipboardCard(
            activity = this,
            sendJson = { sendJson(it) },
            buzz = { buzz(it) },
            onToast = { msg -> android.widget.Toast.makeText(this, msg,
                android.widget.Toast.LENGTH_SHORT).show() },
            onClipboardReceived = { text -> ExtraPages.applyClipboard(text) },
        )
        col.addView(clipboard, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })

        // 电源
        val power = ExtraPages.powerCard(
            activity = this,
            sendJson = { sendJson(it) },
            buzz = { buzz(it) },
            onToast = { msg -> android.widget.Toast.makeText(this, msg,
                android.widget.Toast.LENGTH_SHORT).show() },
        )
        col.addView(power, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })

        // 主机状态
        val (sysCard, updateSys) = ExtraPages.sysInfoCard(this)
        sysInfoUpdater = updateSys
        col.addView(sysCard, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })

        return scroll
    }

    private var sysInfoUpdater: ((JSONObject) -> Unit)? = null

    // ============ 屏幕页 ============
    private fun buildScreenPage(): View {
        ExtraPages.bind(this)
        return ExtraPages.screenPage(
            activity = this,
            hostProvider = { host },
            sendJson = { sendJson(it) },
            buzz = { buzz(it) },
        )
    }

    // ============ 文件页 ============
    private fun buildFilesPage(): View {
        ExtraPages.bind(this)
        return ExtraPages.filesPage(
            activity = this,
            hostProvider = { host },
            sendJson = { sendJson(it) },
            buzz = { buzz(it) },
            onToast = { msg -> android.widget.Toast.makeText(this, msg,
                android.widget.Toast.LENGTH_SHORT).show() },
        )
    }

    // ============ 应用页 ============
    private fun buildAppsPage(): View {
        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(col)

        val head = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        head.addView(sectionTitle("已打开窗口").apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        head.addView(tv("刷新", 12f, C_ACCENT).apply {
            setPadding(dp(8), dp(6), dp(8), dp(6))
            setOnClickListener { loadWindows(); loadPrograms(); buzz("tap") }
        })
        col.addView(head)

        winListBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(winListBox)

        col.addView(sectionTitle("搜索程序").apply { setPadding(dp(2), dp(16), dp(2), dp(7)) })
        appSearch = EditText(this).apply {
            hint = "输入程序名，如 notepad / 微信"
            textSize = 14.5f
            setSingleLine()
            setTextColor(C_TEXT)
            setHintTextColor(C_TEXT3)
            background = strokeBg(C_SURFACE, C_EDGE, 12f)
            setPadding(dp(13), dp(11), dp(13), dp(11))
        }
        appSearch.addTextChangedListener(object : TextWatcher {
            override fun afterTextChanged(s: Editable?) { renderPrograms(s?.toString() ?: "") }
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
        })
        col.addView(appSearch)

        appGrid = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(9), 0, 0)
        }
        col.addView(appGrid)
        return scroll
    }

    private fun loadWindows() = sendJson(JSONObject().put("t", "list_windows"))
    private fun loadPrograms() = sendJson(JSONObject().put("t", "list_apps"))

    private fun renderWindows(list: List<JSONObject>) {
        if (!this::winListBox.isInitialized) return
        winListBox.removeAllViews()
        if (list.isEmpty()) {
            winListBox.addView(tv("没有打开的窗口", 13f, C_TEXT3).apply {
                setPadding(dp(4), dp(8), 0, dp(8))
            })
            return
        }
        list.forEach { w ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = strokeBg(C_SURFACE, C_EDGE, 13f)
                setPadding(dp(11), dp(8), dp(6), dp(8))
            }
            val icon = ImageView(this)
            row.addView(icon, LinearLayout.LayoutParams(dp(26), dp(26)).apply {
                marginEnd = dp(9)
            })
            loadIcon("http://$host:8765/icon?pid=${w.optString("pid")}", icon)

            row.addView(tv(w.optString("title"), 13.5f, C_TEXT).apply {
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
                layoutParams = LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            })

            if (w.optBoolean("foreground")) {
                row.addView(tv("当前", 10f, Color.WHITE).apply {
                    background = bg(C_OK, 20f)
                    setPadding(dp(8), dp(3), dp(8), dp(3))
                })
            } else {
                val focus = tv("⇥", 15f, C_TEXT2).apply {
                    gravity = Gravity.CENTER
                    setPadding(dp(9), dp(4), dp(9), dp(4))
                    setOnClickListener {
                        sendJson(JSONObject().put("t", "focus_window").put("id", w.optLong("id")))
                        buzz("tap")
                        handler.postDelayed({ loadWindows() }, 600)
                    }
                }
                row.addView(focus)
            }
            val minBtn = tv(if (w.optBoolean("minimized")) "▢" else "—", 15f, C_TEXT2).apply {
                gravity = Gravity.CENTER
                setPadding(dp(9), dp(4), dp(9), dp(4))
                setOnClickListener {
                    val key = if (w.optBoolean("minimized")) "focus_window" else "min_window"
                    sendJson(JSONObject().put("t", key).put("id", w.optLong("id")))
                    buzz("tap")
                    handler.postDelayed({ loadWindows() }, 600)
                }
            }
            row.addView(minBtn)
            val closeBtn = tv("✕", 15f, C_DANGER).apply {
                gravity = Gravity.CENTER
                setPadding(dp(9), dp(4), dp(9), dp(4))
                setOnClickListener {
                    sendJson(JSONObject().put("t", "close_window").put("id", w.optLong("id")))
                    buzz("heavy")
                    handler.postDelayed({ loadWindows() }, 1100)
                }
            }
            row.addView(closeBtn)

            winListBox.addView(row, LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { bottomMargin = dp(7) })
        }
    }

    private fun renderPrograms(query: String) {
        if (!this::appGrid.isInitialized) return
        appGrid.removeAllViews()
        val q = query.trim().lowercase()
        val list = allPrograms.filter {
            q.isEmpty() || it.optString("name").lowercase().contains(q)
        }.take(40)
        if (list.isEmpty()) {
            appGrid.addView(tv("没有匹配的程序", 13f, C_TEXT3))
            return
        }
        list.chunked(4).forEach { chunk ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                setPadding(0, 0, 0, dp(9))
            }
            chunk.forEach { p ->
                val name = p.optString("name")
                val tile = LinearLayout(this).apply {
                    orientation = LinearLayout.VERTICAL
                    gravity = Gravity.CENTER
                    background = strokeBg(C_SURFACE, C_EDGE, 14f)
                    setPadding(dp(4), dp(12), dp(4), dp(10))
                    layoutParams = LinearLayout.LayoutParams(0,
                        ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { marginEnd = dp(9) }
                    setOnClickListener {
                        sendJson(JSONObject().put("t", "open_app").put("name", name))
                        buzz("left")
                        handler.postDelayed({ loadWindows() }, 1500)
                    }
                }
                val iv = ImageView(this)
                tile.addView(iv, LinearLayout.LayoutParams(dp(38), dp(38)))
                loadIcon("http://$host:8765/icon?app=${android.net.Uri.encode(name)}", iv)
                tile.addView(tv(name, 11.5f, C_TEXT2).apply {
                    gravity = Gravity.CENTER
                    maxLines = 1
                    ellipsize = TextUtils.TruncateAt.END
                    setPadding(0, dp(6), 0, 0)
                })
                row.addView(tile)
            }
            repeat(4 - chunk.size) {
                row.addView(View(this), LinearLayout.LayoutParams(0, 1, 1f).apply {
                    marginEnd = dp(9)
                })
            }
            appGrid.addView(row)
        }
    }

    private fun loadIcon(url: String, iv: ImageView) {
        synchronized(iconCache) {
            iconCache[url]?.let { iv.setImageBitmap(it); return }
        }
        http.newCall(Request.Builder().url(url).build()).enqueue(object : Callback {
            override fun onFailure(call: Call, e: IOException) {}
            override fun onResponse(call: Call, response: Response) {
                response.use { r ->
                    val bytes = r.body?.bytes() ?: return
                    val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return
                    synchronized(iconCache) { iconCache[url] = bmp }
                    runOnUiThread { iv.setImageBitmap(bmp) }
                }
            }
        })
    }

    // ============ 设置页 ============
    private fun buildSettingsPage(): View {
        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(col)

        val ptr = card()
        ptr.addView(sectionTitle("指针"))
        ptr.addView(sliderRow("移动灵敏度", sens, 0.8f, 3f, 0.1f) { v ->
            sens = v; saveFloat("sens", v)
        })
        ptr.addView(sliderRow("滚动速度", scrollSpd, 1f, 4f, 0.5f) { v ->
            scrollSpd = v; saveFloat("scroll", v)
        })
        col.addView(ptr, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        val fb = card()
        fb.addView(sectionTitle("仿生震动"))
        val vibRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, dp(4), 0, dp(6))
        }
        vibRow.addView(tv("启用震动", 14f, C_TEXT).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        vibRow.addView(Switch(this).apply {
            isChecked = vibOn
            setOnCheckedChangeListener { _, v ->
                vibOn = v
                saveBool("vib", v)
                if (v) buzz("left")
            }
        })
        fb.addView(vibRow)

        val strengthRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, dp(4), 0, dp(4))
        }
        val strengthViews = mutableListOf<TextView>()
        listOf("light" to "轻", "normal" to "标准", "strong" to "强").forEach { pair ->
            val b = tv(pair.second, 12.5f, C_TEXT2).apply {
                gravity = Gravity.CENTER
                setPadding(dp(14), dp(8), dp(14), dp(8))
                background = if (pair.first == vibStrength) strokeBg(C_ACCENT_SOFT, C_ACCENT, 9f)
                              else bg(C_SURFACE2, 9f)
                setTextColor(if (pair.first == vibStrength) C_ACCENT else C_TEXT2)
                setOnClickListener {
                    vibStrength = pair.first
                    saveStr("vibStrength", pair.first)
                    strengthViews.forEachIndexed { i, x ->
                        val key = listOf("light", "normal", "strong")[i]
                        val on = key == pair.first
                        x.setTextColor(if (on) C_ACCENT else C_TEXT2)
                        x.background = if (on) strokeBg(C_ACCENT_SOFT, C_ACCENT, 9f)
                                       else bg(C_SURFACE2, 9f)
                    }
                    buzz("middle")
                }
            }
            strengthViews.add(b)
            strengthRow.addView(b, LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { marginEnd = dp(8) })
        }
        fb.addView(strengthRow)

        val hintRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, dp(8), 0, 0)
        }
        hintRow.addView(tv("帮助水印", 14f, C_TEXT).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        hintRow.addView(Switch(this).apply {
            isChecked = hintOn
            setOnCheckedChangeListener { _, v ->
                hintOn = v
                saveBool("hint", v)
                if (this@MainActivity::hintView.isInitialized) {
                    hintView.visibility = if (v) View.VISIBLE else View.GONE
                    if (v) hintView.alpha = 1f
                }
            }
        })
        fb.addView(hintRow)
        col.addView(fb, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })

        val conn = card()
        conn.addView(sectionTitle("连接"))
        val hostRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, dp(5), 0, dp(5))
        }
        hostRow.addView(tv("电脑", 14f, C_TEXT).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        hostRow.addView(tv(if (host.isEmpty()) "—" else host, 13f, C_TEXT2))
        conn.addView(hostRow)
        val dis = tv("断开连接", 13.5f, C_DANGER).apply {
            gravity = Gravity.CENTER
            background = strokeBg(C_SURFACE2, C_EDGE, 10f)
            setPadding(dp(14), dp(9), dp(14), dp(9))
            setOnClickListener {
                buzz("heavy")
                try { ws?.close(1000, null) } catch (_: Exception) {}
                connected = false
                appRoot.visibility = View.GONE
                connectPage.visibility = View.VISIBLE
                hostInput.setText(host)
            }
        }
        conn.addView(dis, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })
        col.addView(conn, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { topMargin = dp(10) })
        return scroll
    }

    private fun sliderRow(label: String, initial: Float, minV: Float, maxV: Float,
                          step: Float, onChange: (Float) -> Unit): View {
        val wrap = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(6), 0, dp(6))
        }
        val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val valueLabel = tv(String.format("%.1f", initial), 12.5f, C_TEXT2)
        head.addView(tv(label, 14f, C_TEXT).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        })
        head.addView(valueLabel)
        wrap.addView(head)
        val steps = ((maxV - minV) / step).toInt()
        val seek = SeekBar(this).apply { max = steps }
        seek.progress = ((initial - minV) / step).toInt()
        seek.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(sb: SeekBar?, progress: Int, fromUser: Boolean) {
                val v = minV + progress * step
                valueLabel.text = String.format("%.1f", v)
                if (fromUser) onChange(v)
            }
            override fun onStartTrackingTouch(sb: SeekBar?) {}
            override fun onStopTrackingTouch(sb: SeekBar?) { buzz("tap") }
        })
        wrap.addView(seek)
        return wrap
    }

    // ============ WebSocket ============
    private fun connect(target: String) {
        if (target.isEmpty()) return
        ws?.cancel()
        statusText.text = "正在连接 $target …"
        val request = Request.Builder().url("ws://$target:8765/ws").build()
        ws = http.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                runOnUiThread {
                    connected = true
                    host = target
                    saveStr("host", target)
                    connectPage.visibility = View.GONE
                    appRoot.visibility = View.VISIBLE
                    statusDot.setTextColor(C_OK)
                    statusText.text = target
                    buzz("page")
                    sendPing()
                    startPing()
                    sendJson(JSONObject().put("t", "get_volume"))
                    loadWindows()
                    loadPrograms()
                }
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                runOnUiThread { handleMessage(text) }
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                runOnUiThread {
                    connected = false
                    stopPing()
                    statusDot.setTextColor(C_DANGER)
                    if (appRoot.visibility == View.VISIBLE) {
                        statusText.text = "断开，重连中…"
                        handler.postDelayed({ if (!connected) connect(host) }, 2000)
                    } else {
                        statusText.text = "未连接"
                        android.widget.Toast.makeText(this@MainActivity,
                            "连接失败：检查同一 WiFi / 服务端 / 防火墙",
                            android.widget.Toast.LENGTH_LONG).show()
                    }
                }
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                runOnUiThread { connected = false; stopPing() }
            }
        })
    }

    private fun handleMessage(text: String) {
        try {
            val m = JSONObject(text)
            when (m.optString("t")) {
                "pong" -> {
                    val ts = m.optDouble("ts", 0.0).toLong()
                    val lat = max(0L, System.currentTimeMillis() - ts)
                    statusText.text = "$host · ${lat}ms"
                }
                "windows" -> {
                    val arr = m.optJSONArray("list") ?: return
                    renderWindows(jsonList(arr))
                }
                "apps" -> {
                    val arr = m.optJSONArray("list") ?: return
                    allPrograms = jsonList(arr)
                    renderPrograms(appSearch.text?.toString() ?: "")
                }
                "clipboard" -> {
                    val text = m.optString("text", "")
                    ExtraPages.applyClipboard(text)
                    android.widget.Toast.makeText(this,
                        if (text.isEmpty()) "电脑剪贴板为空" else "已读取电脑剪贴板",
                        android.widget.Toast.LENGTH_SHORT).show()
                }
                "sysinfo" -> {
                    m.optJSONObject("info")?.let { sysInfoUpdater?.invoke(it) }
                }
                "volume" -> {
                    val lv = m.optInt("level", -1)
                    val muted = m.optBoolean("muted", false)
                    if (this::volSeek.isInitialized && lv >= 0) {
                        volSeek.progress = lv
                        volNum.text = if (muted) "静音" else "$lv%"
                    }
                }
            }
        } catch (_: Exception) {}
    }

    private fun jsonList(arr: JSONArray): List<JSONObject> =
        (0 until arr.length()).mapNotNull { arr.optJSONObject(it) }

    private fun startPing() {
        stopPing()
        val r = object : Runnable {
            override fun run() {
                sendPing()
                handler.postDelayed(this, 2000)
            }
        }
        pingJob = r
        handler.post(r)
    }

    private fun stopPing() {
        pingJob?.let { handler.removeCallbacks(it) }
        pingJob = null
    }

    private fun sendPing() {
        try {
            ws?.send(JSONObject().put("t", "ping")
                .put("ts", System.currentTimeMillis()).toString())
        } catch (_: Exception) {}
    }

    private fun sendJson(o: JSONObject) {
        if (!connected) return
        try { ws?.send(o.toString()) } catch (_: Exception) {}
    }

    // ---- 二进制输入协议 v2（小端定长小包，热路径零 JSON、零解析）----
    // M dx16 dy16 移动（像素×16）｜ S dy16 滚轮 ｜ K vk flags 键 ｜ B btn flags 鼠标键 ｜ R 释放全部
    private val bin5 = ByteArray(5)
    private val bin3 = ByteArray(3)

    @Suppress("DEPRECATION_ERROR")
    private fun sendBin5(op: Byte, s1: Long, s2: Long) {
        val w = ws ?: return
        bin5[0] = op
        bin5[1] = (s1.toInt() and 0xFF).toByte(); bin5[2] = ((s1.toInt() shr 8) and 0xFF).toByte()
        bin5[3] = (s2.toInt() and 0xFF).toByte(); bin5[4] = ((s2.toInt() shr 8) and 0xFF).toByte()
        try { w.send(okio.ByteString.of(bin5, 0, 5)) } catch (_: Exception) {}
    }

    @Suppress("DEPRECATION_ERROR")
    private fun sendBin3(op: Byte, b1: Int, b2: Int) {
        val w = ws ?: return
        bin3[0] = op; bin3[1] = b1.toByte(); bin3[2] = b2.toByte()
        try { w.send(okio.ByteString.of(bin3, 0, 3)) } catch (_: Exception) {}
    }

    private fun i16(v: Long): Long = v.coerceIn(-32768L, 32767L)

    private fun sendMove(dx: Double, dy: Double) {
        if (!connected) return
        sendBin5(0x4D, i16(Math.round(dx * 16)), i16(Math.round(dy * 16)))
    }

    private fun sendScroll(dy: Double) {
        if (!connected) return
        val v = i16(Math.round(dy * 16)).toInt()
        sendBin3(0x53, v and 0xFF, (v shr 8) and 0xFF)
    }

    private fun sendKeyVk(vk: Int, down: Boolean, repeat: Boolean = false) {
        if (!connected) return
        sendBin3(0x4B, vk, (if (down) 1 else 0) or (if (repeat) 2 else 0))
    }

    private fun sendBtn(btn: Int, down: Boolean) {
        if (!connected) return
        sendBin3(0x42, btn, if (down) 1 else 0)
    }

    @Suppress("DEPRECATION_ERROR")
    private fun sendReleaseAll() {
        if (!connected) return
        try { ws?.send(okio.ByteString.of(byteArrayOf(0x52), 0, 1)) } catch (_: Exception) {}
    }
    private fun sendClick(btn: String) = sendJson(JSONObject().put("t", "click").put("btn", btn))
    private fun sendDown(btn: String) = sendJson(JSONObject().put("t", "down").put("btn", btn))
    private fun sendUp(btn: String) = sendJson(JSONObject().put("t", "up").put("btn", btn))
    private fun sendText(v: String) = sendJson(JSONObject().put("t", "text").put("value", v))


    private fun sendKey(name: String) =
        sendJson(JSONObject().put("t", "key").put("value", name))

    // ============ 触控板手势 ============
    private class PState(var x: Float, var y: Float, var sx: Float, var sy: Float,
                         var startMs: Long, var moved: Boolean)

    private inner class TouchpadView(context: Context) : View(context) {
        private val ptrs = HashMap<Int, PState>()
        private var last: Pair<Float, Float>? = null
        private var scrollRef: Int? = null
        private var scrollAcc = 0f
        private val tapMs = 220L
        private val tapDist = 10f
        private var dragHeld = false
        private var longPressFired = false

        // 低延迟关键：每条 MOVE 事件即时出站，绝不攒帧。
        // 攒帧策略（Choreographer/rAF）最多引入 +16ms——体感明显，已全部移除。
        // 亚像素精度靠协议里的 ×16 定点数 + 服务端余量累积，不会因 <1px 而丢移动。
        @SuppressLint("ClickableViewAccessibility")
        override fun onTouchEvent(e: MotionEvent): Boolean {
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                    for (i in 0 until e.pointerCount) {
                        val id = e.getPointerId(i)
                        if (!ptrs.containsKey(id))
                            ptrs[id] = PState(e.getX(i), e.getY(i), e.getX(i), e.getY(i),
                                System.currentTimeMillis(), false)
                    }
                    val ids = ptrs.keys.toList()
                    if (ids.size == 1) {
                        last = e.getX(0) to e.getY(0)
                        longPressFired = false
                        // 长按 450ms 未移动 = 按住左键（拖拽）
                        handler.postDelayed({
                            val p = ptrs.values.firstOrNull() ?: return@postDelayed
                            if (!p.moved && ptrs.size == 1 && !longPressFired &&
                                System.currentTimeMillis() - p.startMs >= 430) {
                                longPressFired = true
                                dragHeld = true
                                sendBtn(0, true)
                                buzz("left")
                            }
                        }, 450)
                    } else if (ids.size >= 2) {
                        last = null
                        scrollRef = ids[0]
                        scrollAcc = 0f
                    }
                    hintVisible(false)
                }

                MotionEvent.ACTION_MOVE -> {
                    val ids = ptrs.keys.toList()
                    if (ids.size == 1) {
                        val i = e.findPointerIndex(ids[0])
                        if (i >= 0) {
                            val p = ptrs[ids[0]] ?: return true
                            val x = e.getX(i)
                            val y = e.getY(i)
                            if (abs(x - p.sx) > tapDist || abs(y - p.sy) > tapDist) p.moved = true
                            val l = last
                            if (l != null) {
                                val dx = (x - l.first) * sens
                                val dy = (y - l.second) * sens
                                if (dx != 0f || dy != 0f) sendMove(dx.toDouble(), dy.toDouble())
                            }
                            last = x to y
                        }
                    } else if (ids.size >= 2) {
                        val ref = scrollRef
                        if (ref != null) {
                            val i = e.findPointerIndex(ref)
                            if (i >= 0) {
                                val p = ptrs[ref]
                                if (p != null) {
                                    p.moved = true
                                    val y = e.getY(i)
                                    val dy = (y - p.y) * scrollSpd
                                    if (dy != 0f) sendScroll(dy.toDouble())
                                    scrollAcc += abs(dy)
                                    if (scrollAcc > 28f) {
                                        buzz("wheel")
                                        scrollAcc = 0f
                                    }
                                    p.y = y
                                }
                            }
                        }
                    }
                }

                MotionEvent.ACTION_POINTER_UP -> {
                    val id = e.getPointerId(e.actionIndex)
                    val p = ptrs[id]
                    val wasTwo = ptrs.size == 2
                    ptrs.remove(id)
                    if (wasTwo && p != null && !p.moved &&
                        System.currentTimeMillis() - p.startMs < tapMs) {
                        sendBtn(1, true); sendBtn(1, false)
                        buzz("right")
                    }
                    scrollRef = null
                }

                MotionEvent.ACTION_UP -> {
                    if (dragHeld) {
                        sendBtn(0, false)
                        dragHeld = false
                    } else if (ptrs.size == 1) {
                        val p = ptrs.values.first()
                        if (!p.moved && !longPressFired &&
                            System.currentTimeMillis() - p.startMs < tapMs) {
                            sendBtn(0, true); sendBtn(0, false)
                            buzz("left")
                        }
                    }
                    ptrs.clear()
                    last = null
                    scrollRef = null
                    handler.postDelayed({ hintVisible(true) }, 4000)
                }

                MotionEvent.ACTION_CANCEL -> {
                    if (dragHeld) { sendBtn(0, false); dragHeld = false }
                    ptrs.clear()
                    last = null
                    scrollRef = null
                }
            }
            return true
        }
    }
}
