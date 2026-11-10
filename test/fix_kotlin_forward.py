# -*- coding: utf-8 -*-
"""修复 Kotlin 局部函数前向引用问题：screenPage 的 load() 与 filesPage 的 navigate()
在被定义前就被 lambda 引用。改为在函数顶部用 lateinit var 声明。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\java\com\padlink\app\ExtraPages.kt"
s = io.open(P, encoding="utf-8").read()

# ---- screenPage: 把 fun load() 提前声明为变量 ----
s = s.replace(
    '''        val bar = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val info = tv(ctx, "点刷新获取画面", 11.5f, Colors.TEXT3)
        val refresh = chip(ctx, "刷新") { buzz("tap"); load() }''',
    '''        lateinit var load: () -> Unit

        val bar = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val info = tv(ctx, "点刷新获取画面", 11.5f, Colors.TEXT3)
        val refresh = chip(ctx, "刷新") { buzz("tap"); load.invoke() }''')

s = s.replace(
    '''        fun load() {
            val h = hostProvider()''',
    '''        load = fun () {
            val h = hostProvider()''')

# 修正该 lambda 的收尾：原来的 "        }" 在 enqueue 之后需要变成 "        }"
# load = fun () { ... } 形式已经正确闭合，无需额外处理

# autoTick 里调用 load()
s = s.replace('''                if (!autoRunning) return
                load()
                handler.postDelayed(this, 1400)''',
'''                if (!autoRunning) return
                load.invoke()
                handler.postDelayed(this, 1400)''')

s = s.replace('''            if (autoRunning) { load(); handler.post(autoTick) } else handler.removeCallbacks(autoTick)''',
'''            if (autoRunning) { load.invoke(); handler.post(autoTick) } else handler.removeCallbacks(autoTick)''')

s = s.replace('refresh.setOnClickListener { buzz("tap"); load() }',
              'refresh.setOnClickListener { buzz("tap"); load.invoke() }')

# ---- filesPage: navigate / parentPath 提前声明 ----
s = s.replace(
    '''        val bar = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val upBtn = tv(ctx, "↑", 16f, Colors.TEXT2).apply {''',
    '''        var currentPath = ""
        var parentPath: String? = null
        lateinit var navigate: (String) -> Unit

        val bar = LinearLayout(ctx).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val upBtn = tv(ctx, "↑", 16f, Colors.TEXT2).apply {''')

s = s.replace('setOnClickListener { navigate(parentPath ?: "") }',
              'setOnClickListener { navigate.invoke(parentPath ?: "") }')

# 移除后面重复的 currentPath/parentPath 声明
s = s.replace('''        var currentPath = ""
        var parentPath: String? = null

        fun row(entry: JSONObject) {''', '''        fun row(entry: JSONObject) {''')

s = s.replace('if (isDir) navigate(path) else showPreview',
              'if (isDir) navigate.invoke(path) else showPreview')

s = s.replace('''        fun navigate(path: String) {
            val h = hostProvider()''',
'''        navigate = fun (path: String) {
            val h = hostProvider()''')

s = s.replace('        navigate("")\n        return root',
              '        navigate.invoke("")\n        return root')

io.open(P, "w", encoding="utf-8").write(s)
print("Kotlin 前向引用已修复")
print("load 声明:", "lateinit var load" in s)
print("navigate 声明:", "lateinit var navigate" in s)
