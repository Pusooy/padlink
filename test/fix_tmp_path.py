# -*- coding: utf-8 -*-
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\test\push_curl.sh"
s = io.open(P, encoding="utf-8").read()

TMP = "C:/Users/trump/AppData/Local/Temp"
s = s.replace("/tmp/remote_tree.json", TMP + "/remote_tree.json")
s = s.replace("/tmp/push_body.json", TMP + "/push_body.json")
s = s.replace(
    'bodyfile = "/tmp/push_body.json"',
    'bodyfile = "' + TMP + '/push_body.json"',
)
io.open(P, "w", encoding="utf-8").write(s)
print("路径已修正")
for line in s.split("\n"):
    if "remote_tree" in line or "push_body" in line:
        print(" ", line.strip())
