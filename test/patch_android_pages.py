# -*- coding: utf-8 -*-
"""把 ExtraPages 接入 MainActivity：Dock 从 5 页扩到 7 页（+屏幕 +文件），
媒体页嵌入剪贴板/电源/主机状态卡片。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\java\com\padlink\app\MainActivity.kt"
s = io.open(P, encoding="utf-8").read()

# 1) pageKeys 加两项
s = s.replace(
    'private val pageKeys = listOf("pad", "kb", "media", "apps", "settings")',
    'private val pageKeys = listOf("pad", "kb", "media", "apps", "screen", "files", "settings")')

# 2) Dock 标签与图标
s = s.replace(
    '''        val labels = listOf("pad" to "触控板", "kb" to "键盘", "media" to "媒体",
                            "apps" to "应用", "settings" to "设置")
        val glyphs = mapOf("pad" to "◉", "kb" to "⌨", "media" to "▶",
                           "apps" to "▦", "settings" to "⚙")''',
    '''        val labels = listOf("pad" to "触控板", "kb" to "键盘", "media" to "媒体",
                            "apps" to "应用", "screen" to "屏幕", "files" to "文件",
                            "settings" to "设置")
        val glyphs = mapOf("pad" to "◉", "kb" to "⌨", "media" to "▶",
                           "apps" to "▦", "screen" to "▣", "files" to "▤",
                           "settings" to "⚙")''')

# 3) 注册新页面
s = s.replace(
    '''        pages["media"] = buildMediaPage()
        pages["apps"] = buildAppsPage()
        pages["settings"] = buildSettingsPage()''',
    '''        pages["media"] = buildMediaPage()
        pages["apps"] = buildAppsPage()
        pages["screen"] = buildScreenPage()
        pages["files"] = buildFilesPage()
        pages["settings"] = buildSettingsPage()''')

# 4) selectDock 增加分支
s = s.replace(
    '''            "media" -> sendJson(JSONObject().put("t", "get_volume"))
            "apps" -> { loadWindows(); loadPrograms() }''',
    '''            "media" -> {
                sendJson(JSONObject().put("t", "get_volume"))
                sendJson(JSONObject().put("t", "get_sysinfo"))
            }
            "apps" -> { loadWindows(); loadPrograms() }
            "screen" -> Unit
            "files" -> Unit''')

# 5) 新增两个页面构建方法（插在 buildAppsPage 之前）
anchor = "    // ============ 应用页 ============"
assert anchor in s
new_methods = '''    // ============ 屏幕页 ============
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

'''
s = s.replace(anchor, new_methods + anchor)

# 6) 媒体页追加剪贴板/电源/状态卡片
old_media_tail = '''        sys.addView(chips)
        col.addView(sys, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(10)
        })
        return scroll
    }'''
new_media_tail = '''        sys.addView(chips)
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

    private var sysInfoUpdater: ((JSONObject) -> Unit)? = null'''
assert old_media_tail in s
s = s.replace(old_media_tail, new_media_tail, 1)

# 7) 消息处理：clipboard / sysinfo
s = s.replace(
    '''                "volume" -> {''',
    '''                "clipboard" -> {
                    val text = m.optString("text", "")
                    ExtraPages.applyClipboard(text)
                    android.widget.Toast.makeText(this,
                        if (text.isEmpty()) "电脑剪贴板为空" else "已读取电脑剪贴板",
                        android.widget.Toast.LENGTH_SHORT).show()
                }
                "sysinfo" -> {
                    m.optJSONObject("info")?.let { sysInfoUpdater?.invoke(it) }
                }
                "volume" -> {''')

io.open(P, "w", encoding="utf-8").write(s)
print("MainActivity 已接入屏幕页/文件页/剪贴板/电源/主机状态")
