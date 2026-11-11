# -*- coding: utf-8 -*-
"""
Android 17 闪退修复（第二轮）：
- 移除 sensorLandscape 强制横屏（旋转触发崩溃的源头）
- 移除 hideSystemBars（WindowInsetsController 在 Android 16/17 兼容问题）
- 移除 edge-to-edge（API 35+ 行为变更）
- 保留 configChanges 使旋转不重建 Activity
- App 同时支持竖屏和横屏
"""
import io

# ============ Manifest ============
P1 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\AndroidManifest.xml"
m = io.open(P1, encoding="utf-8").read()

# 移除 screenOrientation（让系统自由旋转，消除旋转时的崩溃）
m = m.replace('            android:screenOrientation="sensorLandscape"\n', '')
# 确保 configChanges 完整
if 'android:configChanges' not in m:
    m = m.replace('android:exported="true"',
                  'android:exported="true"\n            android:configChanges="orientation|screenSize|smallestScreenSize|screenLayout|keyboardHidden|density|uiMode|navigation"')

io.open(P1, "w", encoding="utf-8").write(m)
print("Manifest: 移除 sensorLandscape")

# ============ MainActivity ============
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\java\com\padlink\app\MainActivity.kt"
s = io.open(P2, encoding="utf-8").read()

# 移除所有 edge-to-edge / hideSystemBars / requestedOrientation 调用
# 这些在 Android 16/17 旋转时最容易崩溃
replacements = [
    # setupEdgeToEdge 调用
    ("        setupEdgeToEdge()\n", ""),
    # hideSystemBars 调用（onCreate 和 onWindowFocusChanged）
    ("        hideSystemBars()\n", ""),
    ("        if (hasFocus) hideSystemBars()\n", ""),
    # requestedOrientation
    ("        requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE\n", ""),
    # import ActivityInfo
    ("import android.content.pm.ActivityInfo\n", ""),
]

for old, new in replacements:
    if old in s:
        s = s.replace(old, new)
        print(f"  移除: {old.strip()[:50]}")

# 3) 简化/移除 hideSystemBars 和 setupEdgeToEdge 方法（保留定义但内容为空，避免调用错误）
s = s.replace('''    private fun setupEdgeToEdge() {
        try {
            if (Build.VERSION.SDK_INT >= 35) {
                window.setDecorFitsSystemWindows(false)
            }
        } catch (_: Throwable) { }
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
    }''',
'''    private fun setupEdgeToEdge() { /* 已禁用：Android 16/17 兼容 */ }
    private fun hideSystemBars() { /* 已禁用：Android 16/17 兼容 */ }''')

# 移除未使用的 imports
for imp in [
    "import android.view.WindowInsets\n",
    "import android.view.WindowInsetsController\n",
]:
    s = s.replace(imp, "")

io.open(P2, "w", encoding="utf-8").write(s)
print("MainActivity: 已移除横屏强制/系统栏隐藏/edge-to-edge")

# 校验
checks = {
    "sensorLandscape": s.count("sensorLandscape"),
    "hideSystemBars调用": s.count("hideSystemBars()") - s.count("private fun hideSystemBars"),
    "setDecorFitsSystemWindows": s.count("setDecorFitsSystemWindows"),
    "ActivityInfo": s.count("ActivityInfo"),
}
for k, v in checks.items():
    print(f"  {k}: {v}")
