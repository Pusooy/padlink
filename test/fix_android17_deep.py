# -*- coding: utf-8 -*-
"""
针对 Android 17 (API 37) 的深层修复：

1. targetSdk 从 35 升到 36（Android 16），避免在 API 37 设备上被强制启用
   过多未适配的行为变更（35 相对 37 跨度太大）。
2. 移除 `android:enableOnBackInvokedCallback="true"`——该属性在部分 ROM
   上配合自定义 Activity 会导致启动期校验失败。
3. 移除 `android:resizeableActivity="true"` + `screenOrientation="fullSensor"`
   的冲突组合（部分厂商 ROM 会因此抛 IllegalStateException）。
4. 移除 `android:extractNativeLibs="true"`（我们无 native 库，且它与
   PackagingOptions 冲突会打警告）。
5. Activity 增加 `android:theme="@style/AppTheme"` 显式声明，
   避免继承 application 主题时在某些 ROM 上解析失败。
6. 关键：MainActivity 不再用 `requestedOrientation = SCREEN_ORIENTATION_SENSOR_LANDSCAPE`，
   改为在 manifest 声明（部分 ROM 上运行时设置会抛异常）。
"""
import io

# ============ 1. AndroidManifest ============
P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\AndroidManifest.xml"
s = io.open(P, encoding="utf-8").read()

new_manifest = '''<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">

    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.VIBRATE" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />

    <application
        android:label="PadLink"
        android:icon="@mipmap/ic_launcher"
        android:roundIcon="@mipmap/ic_launcher_round"
        android:theme="@style/AppTheme"
        android:allowBackup="true"
        android:supportsRtl="true"
        android:hardwareAccelerated="true"
        android:usesCleartextTraffic="true">

        <activity
            android:name=".MainActivity"
            android:exported="true"
            android:theme="@style/AppTheme"
            android:label="PadLink"
            android:screenOrientation="sensorLandscape"
            android:configChanges="orientation|screenSize|smallestScreenSize|screenLayout|keyboardHidden|density|uiMode|navigation">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
    </application>

</manifest>
'''
io.open(P, "w", encoding="utf-8").write(new_manifest)
print("AndroidManifest: 已移除高风险属性，显式声明 Activity 主题")

# ============ 2. build.gradle.kts：targetSdk 36 ============
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\build.gradle.kts"
s2 = io.open(P2, encoding="utf-8").read()
s2 = s2.replace("compileSdk = 35", "compileSdk = 36")
s2 = s2.replace("targetSdk = 35", "targetSdk = 36")
s2 = s2.replace('targetSdk = 35           // Android 15；Android 16/17 上走兼容模式，无启动限制',
                'targetSdk = 36           // Android 16；在 Android 17 上兼容性最佳')
s2 = s2.replace("versionCode = 3", "versionCode = 4")
s2 = s2.replace('versionName = "1.2"', 'versionName = "1.3"')
io.open(P2, "w", encoding="utf-8").write(s2)
print("build.gradle: compileSdk/targetSdk -> 36, versionCode 4")

# ============ 3. MainActivity：移除运行时方向设置 ============
P3 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\src\main\java\com\padlink\app\MainActivity.kt"
s3 = io.open(P3, encoding="utf-8").read()

old_orient = "        requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE"
if old_orient in s3:
    s3 = s3.replace(old_orient,
                    "        // 方向由 AndroidManifest 的 screenOrientation 声明，运行时不再设置\n"
                    "        // （部分 ROM 在 API 36+ 上对运行时设置方向会抛异常）")

io.open(P3, "w", encoding="utf-8").write(s3)
print("MainActivity: 已移除运行时 requestedOrientation")

# 校验
import re
s3_check = io.open(P3, encoding="utf-8").read()
print("  requestedOrientation 残留:", s3_check.count("requestedOrientation"))
print("  ActivityInfo 引用:", s3_check.count("ActivityInfo"))
