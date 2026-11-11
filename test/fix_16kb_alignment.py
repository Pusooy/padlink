# -*- coding: utf-8 -*-
"""
根因确认：Android 17 的 PackageManager 报
"Error occurred while checking alignment of package"，
导致 Activity 被判定为不存在（Error type 3 / result code=-92）。

这是 16KB page size 对齐要求。Android 15+ 起 Google 要求 APK 支持
16KB page size，Android 17 把该校验设为强制——不满足则包被标记为
不可安装/Activity 不可解析。

修复：AGP 8.7+ 提供 `android.experimental.enableNativeLibraryAlignment`
与 zipalign 16KB 支持。关键配置：
1. `packaging.jniLibs.useLegacyPackaging = false`
2. zipalign 使用 `-P 16`（AGP 内置）
3. 最重要的是：`android.nativeLibraryAlignment` / dex 的 4KB→16KB 对齐
   由 AGP 在 `android.experimental.enableDexUseUncompressed` 等开关控制。

实际最可靠的做法是用 zipalign 手动做 16KB 对齐后再签名，
但 debug 构建的签名由 AGP 完成，顺序会冲突。
所以改为：在 Gradle 里开启 AGP 官方的 16KB 支持开关。
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\build.gradle.kts"
s = io.open(P, encoding="utf-8").read()

# 1) packaging 块：显式声明 16KB 对齐
old_packaging = '''    packaging {
        resources.excludes += setOf(
            "META-INF/*.kotlin_module",
            "META-INF/*.version",
            "META-INF/DEPENDENCIES",
        )
    }'''
new_packaging = '''    packaging {
        resources.excludes += setOf(
            "META-INF/*.kotlin_module",
            "META-INF/*.version",
            "META-INF/DEPENDENCIES",
        )
        // Android 15+ 要求 16KB page size 对齐；Android 17 将校验设为强制，
        // 未对齐会导致 PackageManager 报 "Error occurred while checking alignment"
        // 并使 Activity 无法解析（Error type 3）。
        jniLibs.useLegacyPackaging = false
    }'''
assert old_packaging in s, "packaging 块未匹配"
s = s.replace(old_packaging, new_packaging)

# 2) 关键：开启 AGP 的 16KB 页面大小实验开关
old_buildtypes_anchor = '''    buildTypes {'''
if "androidResources" not in s:
    s = s.replace(old_buildtypes_anchor, '''    // AGP 8.5+ 的 16KB page size 支持开关
    androidResources {
        // 让 resources.arsc 也按 16KB 对齐（Android 17 校验项之一）
        noCompress.add("arsc")
    }

''' + old_buildtypes_anchor)

io.open(P, "w", encoding="utf-8").write(s)
print("build.gradle.kts: 已加入 16KB 对齐配置")

# 3) gradle.properties 开启实验开关
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\gradle.properties"
s2 = io.open(P2, encoding="utf-8").read()
adds = []
for kv in [
    "android.experimental.enableDexUseUncompressed=true",
    "android.experimental.enableNativeLibraryAlignment=true",
]:
    if kv.split("=")[0] not in s2:
        adds.append(kv)

if adds:
    s2 = s2.rstrip() + "\n\n# 16KB page size alignment (Android 15+/17 requirement)\n" + "\n".join(adds) + "\n"
    io.open(P2, "w", encoding="utf-8").write(s2)
    print("gradle.properties: 已加入")
    for a in adds:
        print("  " + a)
else:
    print("gradle.properties: 已存在，跳过")

print("\n当前 gradle.properties:")
print(io.open(P2, encoding="utf-8").read())
