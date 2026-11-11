# -*- coding: utf-8 -*-
"""
移除无用的 androidx.core:core-ktx 依赖。

源码里 grep "import androidx" 结果为空 —— 完全没用到。
但 core-ktx 1.13.1（2024 年版本）会在 Application 启动阶段通过
androidx.startup 的 ContentProvider 自动初始化 ProcessLifecycleOwner 等组件，
其内部 API 在 Android 16/17 上存在行为变更风险，且崩溃发生在
Application.onCreate 之前 → 屏幕看不到任何东西（与用户反馈一致）。

移除后只剩下 okhttp + kotlin-stdlib，启动链路极简。
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\build.gradle.kts"
s = io.open(P, encoding="utf-8").read()

old_deps = '''dependencies {
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("androidx.core:core-ktx:1.13.1")
}'''

new_deps = '''dependencies {
    // 仅保留 OkHttp —— 源码中没有任何 androidx 引用，
    // core-ktx 会在 Application 阶段自动初始化组件，在 Android 16/17 上
    // 有启动期崩溃风险，移除后启动链路最简。
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
}'''

assert old_deps in s, "依赖块未匹配"
s = s.replace(old_deps, new_deps)
io.open(P, "w", encoding="utf-8").write(s)
print("已移除 androidx.core:core-ktx")

# 校验
print("\n当前依赖：")
for line in s.split("\n"):
    if "implementation(" in line:
        print("  " + line.strip())
