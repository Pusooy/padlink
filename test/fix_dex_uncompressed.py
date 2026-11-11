# -*- coding: utf-8 -*-
"""
修正 16KB 对齐：dex 必须是 STORED（不压缩）并按 16KB 对齐。

AGP 的 `packaging.dex.useLegacyPackaging` 控制 dex 是否压缩：
- true  = 传统行为（dex 被 DEFLATED 压缩）← 当前状态，导致 Android 17 拒绝
- false = dex 保持 STORED，AGP 会按页对齐

同时 AGP 8.7 对 16KB page size 的支持需要：
- `android.experimental.enableDexUseUncompressed=true`（已加，但可能不够）
- `packaging.dex.useLegacyPackaging = false` ← 关键，之前漏了

另外 zipalign 的 16KB 对齐由 AGP 内部按 `AlignmentRules` 处理，
只要 dex 是 STORED 就会自动按 16384 对齐。
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\app\build.gradle.kts"
s = io.open(P, encoding="utf-8").read()

# 1) 在 packaging 块加 dex.useLegacyPackaging = false
old = '''        // Android 15+ 要求 16KB page size 对齐；Android 17 将校验设为强制，
        // 未对齐会导致 PackageManager 报 "Error occurred while checking alignment"
        // 并使 Activity 无法解析（Error type 3）。
        jniLibs.useLegacyPackaging = false
    }'''
new = '''        // Android 15+ 要求 16KB page size 对齐；Android 17 将校验设为强制，
        // 未对齐会导致 PackageManager 报 "Error occurred while checking alignment"
        // 并使 Activity 无法解析（Error type 3 / result code=-92）。
        jniLibs.useLegacyPackaging = false
        // 关键：dex 必须 STORED（不压缩）才能按 16KB 对齐
        dex.useLegacyPackaging = false
    }'''
assert old in s, "packaging 块未匹配"
s = s.replace(old, new)

io.open(P, "w", encoding="utf-8").write(s)
print("已添加 packaging.dex.useLegacyPackaging = false")

# 2) 顺便清理无用的 useAndroidX（已无 androidx 依赖）
P2 = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\android\gradle.properties"
s2 = io.open(P2, encoding="utf-8").read()
s2 = s2.replace("android.useAndroidX=true\n", "")
io.open(P2, "w", encoding="utf-8").write(s2)
print("已移除无用的 android.useAndroidX")

print("\n=== packaging 块 ===")
for i, line in enumerate(s.split("\n")):
    if "packaging" in line or "useLegacyPackaging" in line or "noCompress" in line:
        print(f"  {line.strip()}")
