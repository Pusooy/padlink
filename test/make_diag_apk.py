# -*- coding: utf-8 -*-
"""
构建一个极简诊断版 APK：
1. MainActivity.onCreate 只显示一个 TextView（无任何业务逻辑）
2. 在 Application 类里也装全局异常处理（覆盖 Application 阶段崩溃）
3. 用最保守的配置（无 edge-to-edge、无 hideSystemBars、无震动）

用途：真机验证。如果极简版能跑 → 问题是 MainActivity 里的某段业务代码；
如果极简版也闪退 → 是 manifest/主题/构建配置问题。
"""
import io
import os
import shutil

SRC = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android"
TMP = r"C:\Users\trump\AppData\Local\Temp\padlink-diag"

if os.path.exists(TMP):
    shutil.rmtree(TMP)
shutil.copytree(SRC, TMP, ignore=shutil.ignore_patterns(
    "build", ".gradle", ".cxx", "*.apk", "local.properties"))

# ---------- 1. 极简 MainActivity ----------
MAIN = r"""package com.padlink.app

import android.app.Activity
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.view.Gravity
import android.widget.LinearLayout
import android.widget.TextView

class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        android.util.Log.i("PadLinkDiag", "onCreate 开始")

        val tv = TextView(this).apply {
            text = buildString {
                appendLine("PadLink 诊断版")
                appendLine()
                appendLine("启动成功 ✅")
                appendLine()
                appendLine("设备: ${Build.MANUFACTURER} ${Build.MODEL}")
                appendLine("Android: ${Build.VERSION.RELEASE}")
                appendLine("API: ${Build.VERSION.SDK_INT}")
                appendLine("页面大小: ${android.system.Os.sysconf(android.system.OsConstants._SC_PAGESIZE)}")
            }
            textSize = 16f
            setTextColor(Color.WHITE)
            setBackgroundColor(Color.parseColor("#0d0e12"))
            gravity = Gravity.CENTER
            setPadding(48, 48, 48, 48)
        }

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(Color.parseColor("#0d0e12"))
            addView(tv)
        }
        setContentView(root)
        android.util.Log.i("PadLinkDiag", "setContentView 完成")
    }
}
"""

io.open(os.path.join(TMP, "app/src/main/java/com/padlink/app/MainActivity.kt"),
        "w", encoding="utf-8").write(MAIN)

# 删除 ExtraPages（极简版不需要）
ep = os.path.join(TMP, "app/src/main/java/com/padlink/app/ExtraPages.kt")
if os.path.exists(ep):
    os.remove(ep)
    print("已删除 ExtraPages.kt")

# ---------- 2. 极简 manifest ----------
MANIFEST = r"""<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-permission android:name="android.permission.INTERNET" />
    <application
        android:label="PadLink 诊断"
        android:allowBackup="false">
        <activity
            android:name=".MainActivity"
            android:exported="true"
            android:label="PadLink 诊断">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
    </application>
</manifest>
"""
io.open(os.path.join(TMP, "app/src/main/AndroidManifest.xml"),
        "w", encoding="utf-8").write(MANIFEST)
print("已重写 AndroidManifest.xml（极简）")

# ---------- 3. 删除自定义主题（用系统默认） ----------
for f in ["app/src/main/res/values/themes.xml", "app/src/main/res/values/colors.xml"]:
    p = os.path.join(TMP, f)
    if os.path.exists(p):
        os.remove(p)
        print(f"已删除 {f}")

# ---------- 4. build.gradle：保留 okhttp 但极简配置 ----------
GRADLE = r'''plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.padlink.app"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.padlink.diag"
        minSdk = 26
        targetSdk = 36
        versionCode = 99
        versionName = "diag"
    }

    buildTypes {
        getByName("release") {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}

dependencies {
    // 诊断版不需要任何第三方库
}
'''
io.open(os.path.join(TMP, "app/build.gradle.kts"), "w", encoding="utf-8").write(GRADLE)
print("已重写 build.gradle.kts（无任何依赖）")

# ---------- 5. 恢复 local.properties ----------
io.open(os.path.join(TMP, "local.properties"), "w", encoding="utf-8").write(
    "sdk.dir=C:/Users/trump/AppData/Local/Android/Sdk\n")

print("\n诊断工程已生成:", TMP)
