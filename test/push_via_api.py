# -*- coding: utf-8 -*-
"""
通过 GitHub Contents API 推送本地改动（绕过被墙的 github.com:443）。
策略：逐个文件比对本地与远端 blob，只推送变化的文件。
"""
import base64
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

REPO = "Pusooy/padlink"
BRANCH = "main"
ROOT = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink"

SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".gradle", "build", ".vs", "publish"}
SKIP_EXT = {".log", ".dll", ".pdb", ".exe", ".ico", ".apk"}


def get_token():
    out = subprocess.run(
        ["git", "credential", "fill"],
        input="protocol=https\nhost=github.com\n\n",
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    for line in (out.stdout or "").splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1].strip()
    return None


TOKEN = get_token()
if not TOKEN:
    print("无法获取 GitHub token")
    sys.exit(1)

API = "https://api.github.com"


def api(path, method="GET", payload=None, retries=6):
    import time
    url = API + path
    data = json.dumps(payload).encode() if payload is not None else None
    last_err = None
    for attempt in range(retries):
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Authorization", "token " + TOKEN)
        req.add_header("Accept", "application/vnd.github+json")
        req.add_header("Content-Type", "application/json")
        req.add_header("User-Agent", "PadLink-Pusher")
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                body = r.read()
                return r.status, (json.loads(body) if body else None)
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", errors="replace")
            # 4xx 不重试（除 429）
            if e.code not in (429,) and 400 <= e.code < 500:
                return e.code, {"error": body}
            last_err = f"HTTP {e.code}: {body[:100]}"
        except Exception as e:
            last_err = f"{type(e).__name__}: {str(e)[:100]}"
        if attempt < retries - 1:
            time.sleep(2 + attempt * 2)
    return 0, {"error": last_err or "unknown"}


def collect_files():
    items = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            ext = os.path.splitext(fn)[1].lower()
            if ext in SKIP_EXT:
                continue
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, ROOT).replace("\\", "/")
            try:
                size = os.path.getsize(full)
            except OSError:
                continue
            if size > 900_000:
                print(f"  跳过（过大 {size}B）: {rel}")
                continue
            items.append((rel, full))
    return sorted(items)


def get_remote_sha(path):
    code, data = api(f"/repos/{REPO}/contents/{urllib.parse.quote(path)}?ref={BRANCH}")
    if code == 200 and isinstance(data, dict):
        return data.get("sha")
    return None


import urllib.parse

print("收集文件…")
files = collect_files()
print(f"共 {len(files)} 个文件")

# 取当前分支最新 commit
code, ref = api(f"/repos/{REPO}/git/ref/heads/{BRANCH}")
if code != 200:
    print("读取分支失败:", ref)
    sys.exit(1)
head_sha = ref["object"]["sha"]
print("远端 HEAD:", head_sha[:8])

# 构建远端文件树（一次请求拿全部 blob sha）
code, tree = api(f"/repos/{REPO}/git/trees/{head_sha}?recursive=1")
remote = {}
if code == 200:
    for node in tree.get("tree", []):
        if node["type"] == "blob":
            remote[node["path"]] = node["sha"]

# 计算每个本地文件 blob sha（本地计算 git blob hash）
import hashlib


def git_blob_sha(data: bytes) -> str:
    h = hashlib.sha1()
    h.update(b"blob %d\0" % len(data))
    h.update(data)
    return h.hexdigest()


changed = []
for rel, full in files:
    with open(full, "rb") as f:
        data = f.read()
    local_sha = git_blob_sha(data)
    if remote.get(rel) != local_sha:
        changed.append((rel, full, data))

print(f"需更新 {len(changed)} 个文件")

# 逐个提交（每文件一次；GitHub 无批量 contents API）
ok = 0
for i, (rel, full, data) in enumerate(changed, 1):
    payload = {
        "message": f"更新 {rel}",
        "content": base64.b64encode(data).decode(),
        "branch": BRANCH,
    }
    sha = remote.get(rel)
    if sha:
        payload["sha"] = sha
    code, resp = api(f"/repos/{REPO}/contents/{urllib.parse.quote(rel)}",
                     method="PUT", payload=payload)
    if code in (200, 201):
        ok += 1
        print(f"  [{i}/{len(changed)}] ✅ {rel}")
    else:
        print(f"  [{i}/{len(changed)}] ❌ {rel}: {code} {str(resp)[:160]}")

print(f"\n完成：{ok}/{len(changed)} 个文件已推送")
