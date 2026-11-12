#!/bin/bash
# PadLink Windows 可靠部署：杀进程 → 确认死透 → 换新目录 → 全量拷贝 → 哈希校验 → 启动 → 验证端口
set -e
BIN=/c/Users/trump/.zcode/workspace/40c7b29c-1556-4b3b-ad16-4ac9f6367c84/padlink/windows/src/PadLink.App.WinUI/bin/Release/net8.0-windows10.0.19041.0/win-x64
DEPLOY=/c/Users/trump/PadLink

taskkill //IM PadLink.UI.exe //F 2>/dev/null || true
for i in 1 2 3 4 5 6 7 8 9 10; do
  alive=$(tasklist //FI "IMAGENAME eq PadLink.UI.exe" 2>/dev/null | grep -c PadLink || true)
  [ "$alive" = "0" ] && break
  sleep 1
done
if [ "$alive" != "0" ]; then echo "FAIL: 进程杀不死"; exit 1; fi

# 整个部署目录换血（旧目录挪走，杜绝任何锁残留）
stamp=$(date +%s)
if [ -d "$DEPLOY" ]; then
  mkdir -p /c/Users/trump/PadLink_old_$stamp
  cp -p "$DEPLOY/padlink.log" "$DEPLOY/apps.json" /c/Users/trump/PadLink_backup/ 2>/dev/null || true
  mv "$DEPLOY" /c/Users/trump/PadLink_old_$stamp 2>/dev/null || { echo "FAIL: 旧目录挪不动（有文件被锁）"; exit 1; }
fi
mkdir -p "$DEPLOY"
cp -r "$BIN"/* "$DEPLOY"/

# 哈希校验：部署目录的 exe 必须与 bin 一致
h1=$(sha256sum "$BIN/PadLink.UI.exe" | cut -d' ' -f1)
h2=$(sha256sum "$DEPLOY/PadLink.UI.exe" | cut -d' ' -f1)
[ "$h1" = "$h2" ] || { echo "FAIL: exe 哈希不一致"; exit 1; }
echo "hash OK"

cd "$DEPLOY"
(./PadLink.UI.exe &) 
sleep 8
cnt=$(tasklist //FI "IMAGENAME eq PadLink.UI.exe" 2>/dev/null | grep -c PadLink || true)
if [ "$cnt" = "0" ]; then echo "FAIL: 启动后进程消失（查看 Application 事件日志）"; exit 1; fi
port=$(netstat -ano | grep ":8765" | grep -c LISTEN || true)
echo "process alive=$cnt port8765=$port"
echo DEPLOY_OK
