package com.padlink.app

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.text.InputType
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import okhttp3.Call
import okhttp3.Callback
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.URLEncoder

/**
 * 屏幕镜像 / 文件管理 / 剪贴板 / 电源 —— 与网页端能力对齐。
 * 以独立对象提供构建函数，通过回调与 MainActivity 通信，避免主类过度膨胀。
 */
internal object ExtraPages {

    private val http = OkHttpClient()

    // ---------- 通用 ----------
    private fun dp(ctx: Context, v: Number): Int =
        (v.toFloat() * ctx.resources.displayMetrics.density).toInt()

    private fun bg(color: Int, radiusDp: Float = 0f): GradientDrawable =
        GradientDrawable().apply {
            setColor(color)
            cornerRadius = radiusDp
        }

    private fun tv(ctx: Context, text: String, size: Float, color: Int, bold: Boolean = false) =
        TextView(ctx).apply {
            this.text = text
            textSize = size
            setTextColor(color)
            if (bold) typeface = Typeface.DEFAULT_BOLD
        }

    private fun sectionTitle(ctx: Context, text: String) = tv(ctx, text, 11.5f, Colors.TEXT3, true).apply {
        letterSpacing = 0.06f
        setPadding(dp(ctx, 2), dp(ctx, 9), dp(ctx, 2), dp(ctx, 7))
    }

    private fun card(ctx: Context): LinearLayout = LinearLayout(ctx).apply {
        orientation = LinearLayout.VERTICAL
        background = GradientDrawable().apply {
            setColor(Colors.SURFACE)
            setStroke(dp(ctx, 1), Colors.EDGE)
            cornerRadius = dp(ctx, 16).toFloat()
        }
        setPadding(dp(ctx, 14), dp(ctx, 13), dp(ctx, 14), dp(ctx, 13))
    }

    private fun chip(ctx: Context, text: String, onClick: () -> Unit) = tv(ctx, text, 12.5f, Colors.TEXT).apply {
        gravity = Gravity.CENTER
        background = bg(Colors.SURFACE2, dp(ctx, 20).toFloat())
        setPadding(dp(ctx, 12), dp(ctx, 9), dp(ctx, 12), dp(ctx, 9))
        setOnClickListener { onClick() }
    }

    // ================= 屏幕页 =================
    /**
     * 屏幕镜像：定时拉取 /screen 显示；点按画面 → 归一化坐标点击。
     * pollIntervalMs 为 0 时仅手动刷新。
     */
    fun screenPage(
        activity: Activity,
        hostProvider: () -> String,
        sendJson: (JSONObject) -> Unit,
        buzz: (String) -> Unit,
    ): View {
        val ctx = activity
        val root = LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(ctx, 4), 0, 0)
        }

        lateinit var load: () -> Unit

        val bar = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val info = tv(ctx, "点刷新获取画面", 11.5f, Colors.TEXT3)
        val refresh = chip(ctx, "刷新") { buzz("tap"); load.invoke() }
        val autoBtn = chip(ctx, "自动") { }
        bar.addView(info, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        bar.addView(refresh, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { marginEnd = dp(ctx, 8) })
        bar.addView(autoBtn, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        root.addView(bar)

        val wrap = FrameLayout(ctx).apply {
            background = bg(Color.BLACK, dp(ctx, 16).toFloat())
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply { topMargin = dp(ctx, 8) }
        }
        val image = ImageView(ctx).apply { scaleType = ImageView.ScaleType.FIT_CENTER }
        wrap.addView(image, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        val tip = tv(ctx, "点「刷新」获取电脑屏幕", 13f, Colors.TEXT3).apply { gravity = Gravity.CENTER }
        wrap.addView(tip, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER))
        root.addView(wrap)

        var lastBitmap: Bitmap? = null
        var autoRunning = false
        val handler = android.os.Handler(android.os.Looper.getMainLooper())
        val autoTick = object : Runnable {
            override fun run() {
                if (!autoRunning) return
                load.invoke()
                handler.postDelayed(this, 1400)
            }
        }

        load = fun () {
            val h = hostProvider()
            if (h.isEmpty()) return
            val url = "http://$h:8765/screen?w=1280&q=68&t=${System.currentTimeMillis()}"
            http.newCall(Request.Builder().url(url).build()).enqueue(object : Callback {
                override fun onFailure(call: Call, e: IOException) {
                    activity.runOnUiThread { info.text = "截屏失败" }
                }
                override fun onResponse(call: Call, response: Response) {
                    response.use { r ->
                        val bytes = r.body?.bytes() ?: return
                        val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                        if (bmp == null) return
                        activity.runOnUiThread {
                            lastBitmap = bmp
                            image.setImageBitmap(bmp)
                            tip.visibility = View.GONE
                            info.text = "${bmp.width}×${bmp.height} · 点按画面=点击"
                        }
                    }
                }
            })
        }

        autoBtn.setOnClickListener {
            autoRunning = !autoRunning
            autoBtn.setTextColor(if (autoRunning) Colors.ACCENT else Colors.TEXT)
            buzz("page")
            if (autoRunning) { load.invoke(); handler.post(autoTick) } else handler.removeCallbacks(autoTick)
        }
        refresh.setOnClickListener { buzz("tap"); load.invoke() }

        // 点按 → 归一化坐标
        image.setOnTouchListener { v, ev ->
            if (ev.actionMasked == android.view.MotionEvent.ACTION_UP) {
                val bmp = lastBitmap ?: return@setOnTouchListener true
                val vw = v.width.toFloat()
                val vh = v.height.toFloat()
                if (vw <= 0f || vh <= 0f) return@setOnTouchListener true
                // FIT_CENTER 下的实际绘制区域
                val scale = minOf(vw / bmp.width, vh / bmp.height)
                val dw = bmp.width * scale
                val dh = bmp.height * scale
                val ox = (vw - dw) / 2f
                val oy = (vh - dh) / 2f
                val nx = (ev.x - ox) / dw
                val ny = (ev.y - oy) / dh
                if (nx in 0f..1f && ny in 0f..1f) {
                    sendJson(JSONObject().put("t", "screen_click")
                        .put("x", nx.toDouble()).put("y", ny.toDouble()).put("btn", "left"))
                    buzz("left")
                    // 点击波纹
                    val dot = View(ctx).apply {
                        background = GradientDrawable().apply {
                            shape = GradientDrawable.OVAL
                            setColor(Colors.ACCENT)
                        }
                        alpha = 0.75f
                    }
                    val sz = dp(ctx, 22)
                    wrap.addView(dot, FrameLayout.LayoutParams(sz, sz).apply {
                        leftMargin = (ev.x - sz / 2).toInt()
                        topMargin = (ev.y - sz / 2).toInt()
                    })
                    dot.animate().scaleX(2.6f).scaleY(2.6f).alpha(0f).setDuration(380)
                        .withEndAction { wrap.removeView(dot) }.start()
                }
            }
            true
        }
        return root
    }

    // ================= 文件页 =================
    fun filesPage(
        activity: Activity,
        hostProvider: () -> String,
        sendJson: (JSONObject) -> Unit,
        buzz: (String) -> Unit,
        onToast: (String) -> Unit,
    ): View {
        val ctx = activity
        val root = LinearLayout(ctx).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(ctx, 4), 0, 0)
        }

        var currentPath = ""
        var parentPath: String? = null
        lateinit var navigate: (String) -> Unit

        val bar = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val upBtn = tv(ctx, "↑", 16f, Colors.TEXT2).apply {
            gravity = Gravity.CENTER
            background = bg(Colors.SURFACE, dp(ctx, 11).toFloat())
            setOnClickListener { navigate.invoke(parentPath ?: "") }
        }
        val pathView = tv(ctx, "选择位置", 12.5f, Colors.TEXT2).apply {
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.START
            background = bg(Colors.SURFACE, dp(ctx, 11).toFloat())
            setPadding(dp(ctx, 12), dp(ctx, 10), dp(ctx, 12), dp(ctx, 10))
        }
        bar.addView(upBtn, LinearLayout.LayoutParams(dp(ctx, 40), dp(ctx, 38)).apply {
            marginEnd = dp(ctx, 8)
        })
        bar.addView(pathView, LinearLayout.LayoutParams(0,
            ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        root.addView(bar)

        val scroll = ScrollView(ctx).apply {
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply { topMargin = dp(ctx, 8) }
        }
        val list = LinearLayout(ctx).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(list)
        root.addView(scroll)

        fun row(entry: JSONObject) {
            val isDir = entry.optBoolean("dir")
            val name = entry.optString("name")
            val path = entry.optString("path")
            val r = LinearLayout(ctx).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = GradientDrawable().apply {
                    setColor(Colors.SURFACE)
                    setStroke(dp(ctx, 1), Colors.EDGE)
                    cornerRadius = dp(ctx, 12).toFloat()
                }
                setPadding(dp(ctx, 12), dp(ctx, 10), dp(ctx, 8), dp(ctx, 10))
            }
            val iconChar = when {
                isDir -> "▸"
                entry.optString("ext") in listOf("jpg", "jpeg", "png", "gif", "webp", "bmp") -> "▣"
                entry.optString("ext") in listOf("mp4", "webm", "mkv", "mov", "avi") -> "▶"
                entry.optString("ext") in listOf("mp3", "wav", "flac", "m4a", "ogg") -> "♪"
                else -> "▤"
            }
            r.addView(tv(ctx, iconChar, 14f, Colors.ACCENT))
            val nameView = tv(ctx, if (isDir) "$name/" else name, 13.5f, Colors.TEXT).apply {
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.MIDDLE
                setPadding(dp(ctx, 10), 0, dp(ctx, 8), 0)
            }
            r.addView(nameView, LinearLayout.LayoutParams(0,
                ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            if (!isDir) {
                r.addView(tv(ctx, fmtSize(entry.optLong("size")), 11f, Colors.TEXT3))
                val open = tv(ctx, "↗", 14f, Colors.TEXT2).apply {
                    gravity = Gravity.CENTER
                    setPadding(dp(ctx, 10), dp(ctx, 2), dp(ctx, 6), dp(ctx, 2))
                    setOnClickListener {
                        sendJson(JSONObject().put("t", "open_file").put("path", path))
                        buzz("tap")
                        onToast("已让电脑打开")
                    }
                }
                r.addView(open)
            }
            r.setOnClickListener {
                buzz("tap")
                if (isDir) navigate.invoke(path) else showPreview(activity, hostProvider(), path, name, entry.optString("ext")) { buzz(it) }
            }
            list.addView(r, LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { bottomMargin = dp(ctx, 7) })
        }

        fun renderRoots(roots: JSONArray) {
            list.removeAllViews()
            for (i in 0 until roots.length()) {
                val o = roots.optJSONObject(i) ?: continue
                val fake = JSONObject()
                    .put("name", o.optString("name"))
                    .put("path", o.optString("path"))
                    .put("dir", true)
                row(fake)
            }
        }

        navigate = fun (path: String) {
            val h = hostProvider()
            if (h.isEmpty()) return
            val url = "http://$h:8765/files?path=" + URLEncoder.encode(path, "UTF-8")
            http.newCall(Request.Builder().url(url).build()).enqueue(object : Callback {
                override fun onFailure(call: Call, e: IOException) {
                    activity.runOnUiThread { onToast("读取目录失败") }
                }
                override fun onResponse(call: Call, response: Response) {
                    response.use { r ->
                        val body = r.body?.string() ?: return
                        val obj = runCatching { JSONObject(body) }.getOrNull() ?: return
                        val err = obj.optString("error")
                        activity.runOnUiThread {
                            if (err.isNotEmpty()) { onToast(err); return@runOnUiThread }
                            currentPath = obj.optString("path")
                            parentPath = if (obj.isNull("parent")) null else obj.optString("parent")
                            pathView.text = if (currentPath.isEmpty()) "选择位置" else currentPath
                            val roots = obj.optJSONArray("roots")
                            if (roots != null && currentPath.isEmpty()) {
                                renderRoots(roots)
                            } else {
                                list.removeAllViews()
                                val entries = obj.optJSONArray("entries")
                                if (entries == null || entries.length() == 0) {
                                    list.addView(tv(ctx, "空目录", 13f, Colors.TEXT3))
                                } else {
                                    for (i in 0 until entries.length()) {
                                        entries.optJSONObject(i)?.let { row(it) }
                                    }
                                }
                            }
                        }
                    }
                }
            })
        }

        navigate.invoke("")
        return root
    }

    private fun fmtSize(n: Long): String = when {
        n <= 0 -> ""
        n < 1024 -> "$n B"
        n < 1048576 -> "${n / 1024} KB"
        n < 1073741824 -> String.format("%.1f MB", n / 1048576.0)
        else -> String.format("%.2f GB", n / 1073741824.0)
    }

    private val TEXT_EXT = setOf(
        "txt", "md", "log", "json", "xml", "csv", "js", "ts", "py", "cs", "java", "c", "cpp",
        "h", "html", "css", "sql", "bat", "ps1", "ini", "yml", "yaml", "cfg")

    /** 兼容旧调用（当前无内部状态需绑定）。 */
    fun bind(activity: Activity) { /* no-op */ }

    // ================= 剪贴板（嵌入媒体页） =================
    fun clipboardCard(
        activity: Activity,
        sendJson: (JSONObject) -> Unit,
        buzz: (String) -> Unit,
        onToast: (String) -> Unit,
        onClipboardReceived: (String) -> Unit,
    ): View {
        val ctx = activity
        val c = card(ctx)
        c.addView(sectionTitle(ctx, "剪贴板"))
        val area = EditText(ctx).apply {
            hint = "剪贴板内容（可编辑后发送）"
            textSize = 13f
            setTextColor(Colors.TEXT)
            setHintTextColor(Colors.TEXT3)
            setBackgroundColor(Color.TRANSPARENT)
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
            minLines = 2
            maxLines = 4
            setPadding(dp(ctx, 10), dp(ctx, 8), dp(ctx, 10), dp(ctx, 8))
        }
        val areaWrap = FrameLayout(ctx).apply {
            background = bg(Colors.SURFACE2, dp(ctx, 11).toFloat())
            addView(area, FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
        c.addView(areaWrap, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        clipboardTarget = area

        val row = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, dp(ctx, 9), 0, 0)
        }
        row.addView(chip(ctx, "取电脑") {
            sendJson(JSONObject().put("t", "get_clipboard")); buzz("tap")
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginEnd = dp(ctx, 8) })
        row.addView(chip(ctx, "发到电脑") {
            sendJson(JSONObject().put("t", "set_clipboard").put("text", area.text.toString()))
            buzz("left"); onToast("已发送到电脑剪贴板")
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginEnd = dp(ctx, 8) })
        row.addView(chip(ctx, "粘贴") {
            sendJson(JSONObject().put("t", "key").put("value", "ctrl+v")); buzz("tap")
        })
        c.addView(row)
        return c
    }

    private var clipboardTarget: EditText? = null

    /** 收到 clipboard 消息时回填（由 MainActivity 调用）。 */
    fun applyClipboard(text: String) {
        clipboardTarget?.setText(text)
    }

    // ================= 电源（嵌入媒体页） =================
    fun powerCard(
        activity: Activity,
        sendJson: (JSONObject) -> Unit,
        buzz: (String) -> Unit,
        onToast: (String) -> Unit,
    ): View {
        val ctx = activity
        val c = card(ctx)
        c.addView(sectionTitle(ctx, "电源"))
        val row = LinearLayout(ctx).apply { orientation = LinearLayout.HORIZONTAL }

        fun add(label: String, action: String, danger: Boolean = false) {
            val b = tv(ctx, label, 12.5f, if (danger) Colors.DANGER else Colors.TEXT).apply {
                gravity = Gravity.CENTER
                background = bg(Colors.SURFACE2, dp(ctx, 20).toFloat())
                setPadding(dp(ctx, 12), dp(ctx, 9), dp(ctx, 12), dp(ctx, 9))
                setOnClickListener {
                    if (danger) {
                        AlertDialog.Builder(ctx)
                            .setMessage("确定要$label 电脑吗？")
                            .setPositiveButton("确定") { _, _ ->
                                sendJson(JSONObject().put("t", "power").put("action", action))
                                buzz("heavy"); onToast("已发送：$label")
                            }
                            .setNegativeButton("取消", null)
                            .show()
                    } else {
                        sendJson(JSONObject().put("t", "power").put("action", action))
                        buzz("heavy"); onToast("已发送：$label")
                    }
                }
            }
            row.addView(b, LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { marginEnd = dp(ctx, 8) })
        }
        add("锁定", "lock")
        add("睡眠", "sleep")
        add("重启", "restart", danger = true)
        add("关机", "shutdown", danger = true)
        c.addView(row)
        return c
    }

    // ================= 主机状态（嵌入媒体页） =================
    fun sysInfoCard(activity: Activity): Pair<View, (JSONObject) -> Unit> {
        val ctx = activity
        val c = card(ctx)
        c.addView(sectionTitle(ctx, "主机状态"))
        val box = LinearLayout(ctx).apply { orientation = LinearLayout.HORIZONTAL }
        val placeholder = tv(ctx, "打开本页自动获取", 12.5f, Colors.TEXT3)
        box.addView(placeholder)
        c.addView(box)
        val update: (JSONObject) -> Unit = { info ->
            box.removeAllViews()
            val items = mutableListOf<Pair<String, String>>()
            items.add("CPU" to info.optString("cpu", "—"))
            items.add("内存" to info.optString("ram", "—"))
            items.add("运行" to info.optString("uptime", "—"))
            val bat = info.optString("battery", "—")
            if (bat != "—" && bat.isNotEmpty()) items.add("电量" to bat)
            items.forEachIndexed { i, kv ->
                box.addView(tv(ctx, "${kv.first} ${kv.second}", 11.5f, Colors.TEXT2).apply {
                    background = bg(Colors.SURFACE2, dp(ctx, 20).toFloat())
                    setPadding(dp(ctx, 10), dp(ctx, 6), dp(ctx, 10), dp(ctx, 6))
                }, LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                    .apply { if (i < items.lastIndex) marginEnd = dp(ctx, 7) })
            }
        }
        return c to update
    }

    /** 预览弹窗：图片用 ImageView，文本用 TextView。 */
    fun showPreview(
        activity: Activity,
        host: String,
        path: String,
        name: String,
        ext: String,
        buzz: (String) -> Unit,
    ) {
        val ctx = activity
        val builder = AlertDialog.Builder(ctx).setTitle(name)

        when {
            ext in listOf("jpg", "jpeg", "png", "gif", "webp", "bmp") -> {
                val iv = ImageView(ctx).apply {
                    adjustViewBounds = true
                    scaleType = ImageView.ScaleType.FIT_CENTER
                    setPadding(dp(ctx, 8), dp(ctx, 8), dp(ctx, 8), dp(ctx, 8))
                }
                val url = "http://$host:8765/file?path=" + URLEncoder.encode(path, "UTF-8")
                http.newCall(Request.Builder().url(url).build()).enqueue(object : Callback {
                    override fun onFailure(call: Call, e: IOException) {}
                    override fun onResponse(call: Call, response: Response) {
                        response.use { r ->
                            val bytes = r.body?.bytes() ?: return
                            val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return
                            activity.runOnUiThread { iv.setImageBitmap(bmp) }
                        }
                    }
                })
                builder.setView(iv)
            }
            ext in TEXT_EXT -> {
                val tvView = TextView(ctx).apply {
                    textSize = 12.5f
                    setTextColor(Colors.TEXT)
                    setPadding(dp(ctx, 14), dp(ctx, 12), dp(ctx, 14), dp(ctx, 12))
                    typeface = Typeface.MONOSPACE
                }
                val sv = ScrollView(ctx).apply { addView(tvView) }
                val url = "http://$host:8765/file?path=" + URLEncoder.encode(path, "UTF-8")
                http.newCall(Request.Builder().url(url).build()).enqueue(object : Callback {
                    override fun onFailure(call: Call, e: IOException) {
                        activity.runOnUiThread { tvView.text = "读取失败" }
                    }
                    override fun onResponse(call: Call, response: Response) {
                        response.use { r ->
                            val body = r.body?.string() ?: return
                            val obj = runCatching { JSONObject(body) }.getOrNull()
                            val text = obj?.optString("text") ?: body
                            activity.runOnUiThread { tvView.text = text }
                        }
                    }
                })
                builder.setView(sv)
            }
            else -> {
                builder.setMessage("该类型不支持在线预览，可让电脑打开。")
            }
        }

        builder.setPositiveButton("让电脑打开") { _, _ ->
            // 由调用方发送；此处仅提示（实际发送在 filesPage 的 row 中）
        }
        builder.setNegativeButton("关闭", null)
        builder.show()
        buzz("tap")
    }
}

/** 与主界面共享的配色令牌。 */
internal object Colors {
    val BG = Color.parseColor("#0d0e12")
    val SURFACE = Color.parseColor("#15171d")
    val SURFACE2 = Color.parseColor("#1b1e26")
    val EDGE = Color.parseColor("#252935")
    val TEXT = Color.parseColor("#e9eaef")
    val TEXT2 = Color.parseColor("#9aa1ad")
    val TEXT3 = Color.parseColor("#6b7280")
    val ACCENT = Color.parseColor("#4c86ff")
    val OK = Color.parseColor("#34d399")
    val DANGER = Color.parseColor("#f87171")
}
