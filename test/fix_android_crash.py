# -*- coding: utf-8 -*-
"""
MainActivity 闪退加固：
1. buildUi 整体 try/catch，失败时显示可复制的错误信息（而非闪退）
2. 全局异常处理不再依赖 runOnUiThread（可能本身就在主线程崩溃循环）
3. 增加 logcat 可读的完整堆栈
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\java\com\padlink\app\MainActivity.kt"
s = io.open(P, encoding="utf-8").read()

# 1) onCreate 里 buildUi 加保护
old = "        loadPrefs()\n        buildUi()"
new = '''        loadPrefs()
        try {
            buildUi()
        } catch (fatal: Throwable) {
            android.util.Log.e("PadLink", "buildUi 失败", fatal)
            showFatalError(fatal)
        }'''
assert old in s, "buildUi 调用未找到"
s = s.replace(old, new)

# 2) 追加 showFatalError：用一个纯 TextView 显示错误，绝不抛异常
if "private fun showFatalError" not in s:
    anchor = "    private fun hideSystemBars() {"
    assert anchor in s
    fatal_fn = '''    /**
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
                isTextSelectable = true
            }
            val sv = ScrollView(this).apply { addView(tv) }
            setContentView(sv)
        } catch (_: Throwable) {
            // 连错误界面都建不出来，只能静默
        }
    }

'''
    s = s.replace(anchor, fatal_fn + anchor)

# 3) 全局异常处理：改为写 log + 尝试展示，不再依赖 runOnUiThread 嵌套
old_handler = '''        // 兜底：任何初始化异常都弹窗提示而不是闪退
        Thread.setDefaultUncaughtExceptionHandler { _, ex ->
            try {
                android.util.Log.e("PadLink", "未捕获异常", ex)
                runOnUiThread {
                    android.app.AlertDialog.Builder(this)
                        .setTitle("PadLink 启动异常")
                        .setMessage(ex.stackTraceToString().take(1200))
                        .setPositiveButton("确定", null)
                        .show()
                }
            } catch (_: Throwable) { }
        }'''
new_handler = '''        // 兜底：记录完整堆栈（可通过 adb logcat -s PadLink 查看）
        Thread.setDefaultUncaughtExceptionHandler { thread, ex ->
            android.util.Log.e("PadLink", "未捕获异常 @ ${thread.name}", ex)
            try { showFatalError(ex) } catch (_: Throwable) { }
        }'''
assert old_handler in s
s = s.replace(old_handler, new_handler)

io.open(P, "w", encoding="utf-8").write(s)
print("MainActivity: 闪退兜底已加固")
