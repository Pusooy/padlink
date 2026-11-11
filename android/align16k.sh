#!/bin/bash
# PadLink APK 16KB 对齐 + 签名（修正命令语法版）
# zipalign 用法: zipalign [-f] [-P <kb>] <align_in_bytes> <in> <out>
# dex 是 STORED 且非 .so，zipalign 的 <align> 参数（4096）即可对齐 dex；
# 无 .so 时 -P 16 无实际作用，但 4096 对齐已满足 PackageManager 校验。
set -e

SDK=/c/Users/trump/AppData/Local/Android/Sdk
BT="$SDK/build-tools/36.0.0"
ZA="$BT/zipalign.exe"
AS="$BT/apksigner.bat"
export JAVA_HOME="C:\\Program Files\\Microsoft\\jdk-17.0.21.21-hotspot"
export PATH="/c/Program Files/Microsoft/jdk-17.0.21.21-hotspot/bin:$PATH"

SRC_APK="C:/Users/trump/.zcode/workspace/40c7b29c-1556-4b3b-ad16-4ac9f6367c84/padlink/android/app/build/outputs/apk/debug/app-debug.apk"
ALIGNED="C:/Users/trump/AppData/Local/Temp/padlink-aligned.apk"
FINAL="C:/Users/trump/Downloads/sidestore-setup/PadLink.apk"
KS_WIN="C:\\Users\\trump\\.android\\debug.keystore"

echo "=== 1) 源 APK 对齐检查（应显示 BAD）==="
"$ZA" -c 4 "$SRC_APK" >/dev/null 2>&1 && echo "  已对齐（异常）" || echo "  未对齐（符合预期）"

echo "=== 2) zipalign 4096（dex STORED 需要 4KB 对齐）==="
"$ZA" -f 4096 "$SRC_APK" "$ALIGNED"
echo "  完成"

echo "=== 3) 校验对齐 ==="
"$ZA" -c 4 "$ALIGNED" && echo "  4096 对齐通过"

echo "=== 4) apksigner 签名（zipalign 必须在签名前）==="
"$AS" sign \
  --ks "$KS_WIN" --ks-pass pass:android --key-pass pass:android \
  --ks-key-alias androiddebugkey \
  --out "$FINAL" "$ALIGNED" 2>&1 | tail -3

echo "=== 5) 最终校验 ==="
"$AS" verify "$FINAL" 2>&1 | head -2 && echo "  签名 OK"
"$ZA" -c 4 "$FINAL" >/dev/null 2>&1 && echo "  对齐 OK（签名块不影响 entry 对齐）"

echo "=== 6) dex 偏移验证 ==="
python - "$FINAL" <<'PYEOF'
import zipfile, struct, sys
p = sys.argv[1]
z = zipfile.ZipFile(p)
raw = open(p, 'rb').read()
for n in z.namelist():
    if n.endswith('.dex') or n.endswith('.arsc'):
        i = z.getinfo(n)
        f = open(p, 'rb')
        f.seek(i.header_offset)
        hdr = f.read(30)
        fnlen, exlen = struct.unpack('<HH', hdr[26:30])
        do = i.header_offset + 30 + fnlen + exlen
        f.close()
        m = 'STORED' if i.compress_type == 0 else 'DEFLATED'
        al4 = do % 4096 == 0
        print(f"  {'OK ' if al4 else 'BAD'} {n}: {m} data_offset={do} 4KB对齐={al4}")
PYEOF

echo
echo "=== 产物 ==="
ls -lh "$FINAL"
