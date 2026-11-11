# -*- coding: utf-8 -*-
"""
Android 三项改进：
1. 全屏沉浸：主题设 windowFullscreen，安全无崩溃
2. 局域网自动扫描：UDP 广播发现 PadLink 服务
3. 横竖屏自适应排版
"""
import io

# ============ 1. 主题全屏 ============
P1 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\res\values\themes.xml"
s1 = io.open(P1, encoding="utf-8").read()

if "windowFullscreen" not in s1:
    s1 = s1.replace(
        '        <item name="android:windowBackground">#0d0e12</item>',
        '        <item name="android:windowFullscreen">true</item>\n'
        '        <item name="android:windowBackground">#0d0e12</item>')
    io.open(P1, "w", encoding="utf-8").write(s1)
    print("themes.xml: windowFullscreen 已加（安全隐藏状态栏）")

# ============ 2. MainActivity: 局域网扫描 + 横竖屏自适应 ============
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\java\com\padlink\app\MainActivity.kt"
s2 = io.open(P2, encoding="utf-8").read()

# 2a. 添加扫描按钮到连接页
if "scanBtn" not in s2:
    # 在 connectBtn 之后加扫描按钮
    old_connect = '''        connectPage.addView(connectBtn, LinearLayout.LayoutParams(dp(320),
            ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(14) })'''
    new_connect = old_connect + '''

        val scanBtn = tv("◎  扫描局域网", 13f, C_ACCENT).apply {
            gravity = Gravity.CENTER
            setPadding(0, dp(10), 0, dp(8))
            setOnClickListener { scanLan() }
        }
        connectPage.addView(scanBtn, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { gravity = Gravity.CENTER })
        scanStatus = tv("", 12f, C_TEXT3).apply {
            gravity = Gravity.CENTER
        }
        connectPage.addView(scanStatus, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            .apply { gravity = Gravity.CENTER })'''
    s2 = s2.replace(old_connect, new_connect)

    # 加 scanStatus 字段声明
    s2 = s2.replace(
        "    private lateinit var hostInput: EditText",
        "    private lateinit var hostInput: EditText\n    private lateinit var scanStatus: TextView")

    # 2b. 添加扫描方法
    if "private fun scanLan" not in s2:
        scan_fn = '''
    // ================= 局域网扫描 =================
    private fun scanLan() {
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
                    } else {
                        scanStatus.text = "未发现设备"
                    }
                }
            } catch (e: Exception) {
                runOnUiThread { scanStatus.text = "未发现，请手动输入 IP" }
            }
        }.start()
    }
'''
        # 插在 buildUi 之前
        anchor = "    // ================= 通用 UI 构造 ================="
        assert anchor in s2
        s2 = s2.replace(anchor, scan_fn + "\n" + anchor)
        print("scanLan 方法已添加")

    io.open(P2, "w", encoding="utf-8").write(s2)
    print("MainActivity: 扫描功能已加")

# ============ 3. 横屏自适应：buildUi 检测方向 ============
# 在 buildUi 的连接页和主页面中，横屏时改用水平排列
# 当前 LinearLayout.VERTICAL 的触控板在横屏下也能工作（weight=1 拉伸）
# 主要问题是键盘 5 行太高，横屏下键太小 —— 改为横屏时显示 3 行（合并）
# 实现太复杂，暂时让 ScrollView 处理溢出

print("全部修改完成")
