# -*- coding: utf-8 -*-
"""打包 payload.zip：把 UI publish 目录的内容（不带顶层目录）压进 zip。"""
import os
import sys
import zipfile

src = sys.argv[1]
dst = sys.argv[2]

if os.path.exists(dst):
    os.remove(dst)

src = os.path.abspath(src)
count = 0
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for root, dirs, files in os.walk(src):
        for f in files:
            full = os.path.join(root, f)
            rel = os.path.relpath(full, src).replace(os.sep, "/")
            z.write(full, rel)
            count += 1

size_mb = os.path.getsize(dst) / 1024 / 1024
print(f"payload.zip: {count} 个文件, {size_mb:.1f} MB")
