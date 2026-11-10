#!/bin/bash
# 通过 GitHub Contents API + curl 推送改动（绕过 github.com:443 被墙）
set -u
ROOT="/c/Users/trump/.zcode/workspace/40c7b29c-1556-4b3b-ad16-4ac9f6367c84/padlink"
REPO="Pusooy/padlink"
BRANCH="main"
TOKEN=$(printf "protocol=https\nhost=github.com\n\n" | git credential fill 2>/dev/null | grep "^password=" | cut -d= -f2)

if [ -z "$TOKEN" ]; then echo "无 token"; exit 1; fi

api() {
  local method="$1" path="$2" body="${3:-}"
  local args=(-sS --ssl-no-revoke --max-time 60 -X "$method"
    -H "Authorization: token $TOKEN"
    -H "Accept: application/vnd.github+json"
    -H "Content-Type: application/json"
    -H "User-Agent: PadLink-Pusher")
  if [ -n "$body" ]; then args+=(-d "$body"); fi
  curl "${args[@]}" "https://api.github.com$path"
}

# 取远端 HEAD
HEAD_SHA=$(api GET "/repos/$REPO/git/ref/heads/$BRANCH" | python -c "
import json,sys
try:
    d=json.load(sys.stdin); print(d['object']['sha'])
except Exception: print('')")
if [ -z "$HEAD_SHA" ]; then echo "读取分支失败"; exit 1; fi
echo "远端 HEAD: ${HEAD_SHA:0:8}"

# 取远端文件树
TREE_JSON=$(api GET "/repos/$REPO/git/trees/$HEAD_SHA?recursive=1")
echo "$TREE_JSON" > C:/Users/trump/AppData/Local/Temp/remote_tree.json

# 比对并推送
python - "$ROOT" "$REPO" "$BRANCH" "$TOKEN" <<'PYEOF'
import base64, hashlib, json, os, subprocess, sys, urllib.parse

ROOT, REPO, BRANCH, TOKEN = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".gradle", "build", ".vs", "publish"}
SKIP_EXT = {".log", ".dll", ".pdb", ".exe", ".ico", ".apk"}

remote = {}
with open("C:/Users/trump/AppData/Local/Temp/remote_tree.json", encoding="utf-8") as f:
    tree = json.load(f)
for node in tree.get("tree", []):
    if node.get("type") == "blob":
        remote[node["path"]] = node["sha"]

def blob_sha(data):
    h = hashlib.sha1()
    h.update(b"blob %d\0" % len(data))
    h.update(data)
    return h.hexdigest()

changed = []
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
    for fn in filenames:
        if os.path.splitext(fn)[1].lower() in SKIP_EXT:
            continue
        full = os.path.join(dirpath, fn)
        rel = os.path.relpath(full, ROOT).replace("\\", "/")
        try:
            data = open(full, "rb").read()
        except OSError:
            continue
        if len(data) > 900_000:
            print(f"  跳过过大: {rel}")
            continue
        if remote.get(rel) != blob_sha(data):
            changed.append((rel, data, remote.get(rel)))

print(f"共 {len(changed)} 个文件需更新")

ok = 0
for i, (rel, data, sha) in enumerate(changed, 1):
    body = {
        "message": f"update {rel}",
        "content": base64.b64encode(data).decode(),
        "branch": BRANCH,
    }
    if sha:
        body["sha"] = sha
    bodyfile = "C:/Users/trump/AppData/Local/Temp/push_body.json"
    with open(bodyfile, "w", encoding="utf-8") as f:
        json.dump(body, f)

    url = f"https://api.github.com/repos/{REPO}/contents/" + urllib.parse.quote(rel)
    proc = subprocess.run([
        "curl", "-sS", "--ssl-no-revoke", "--max-time", "90", "-X", "PUT",
        "-H", f"Authorization: token {TOKEN}",
        "-H", "Accept: application/vnd.github+json",
        "-H", "Content-Type: application/json",
        "-H", "User-Agent: PadLink-Pusher",
        "--data-binary", "@" + bodyfile,
        url,
    ], capture_output=True, text=True, encoding="utf-8", errors="replace")
    out = (proc.stdout or "").strip()
    if '"content"' in out or '"commit"' in out:
        ok += 1
        print(f"  [{i}/{len(changed)}] OK {rel}")
    else:
        print(f"  [{i}/{len(changed)}] FAIL {rel}: {out[:120]}")

print(f"\n完成 {ok}/{len(changed)}")
PYEOF
