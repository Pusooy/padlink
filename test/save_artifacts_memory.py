# -*- coding: utf-8 -*-
import io

P = (r"C:\Users\trump\.zcode\cli\memories\projects"
     r"\40c7b29c-1556-4b3b-ad16-4ac9f6367c84-8b8417c80882ffc3\memory"
     r"\padlink-artifacts.md")

content = """---
name: padlink-artifacts
description: PadLink 各客户端单文件产物路径 —— 用户明确要求每次交付都要主动告知这些路径
metadata:
  type: project
---

用户明确要求：**以后每次交付 PadLink 相关改动，都要主动列出各客户端单文件产物的位置。**

## 产物清单（截至 2026-11-10）

| 客户端 | 单文件产物 | 说明 |
|---|---|---|
| **Windows 上位机** | `C:\\Users\\trump\\PadLink\\PadLink.exe` | ~74 MB，自包含 + 前端全嵌入，**唯一文件**，无任何依赖目录 |
| 上位机（构建中间产物） | `padlink\\app-wpf\\bin\\Release\\net8.0-windows\\win-x64\\publish\\PadLink.exe` | publish 输出，只有一个 exe |
| **安卓** | `C:\\Users\\trump\\Downloads\\sidestore-setup\\PadLink.apk` | ~1.4 MB debug APK，GitHub Actions `Build APK` 产物 |
| **网页 PWA** | 无需文件，由 Windows exe 内嵌提供 | 手机浏览器访问 `http://<PC-IP>:8765` |

## 构建命令

```bash
# Windows（纯单文件，必须用 publish 而非 build）
cd padlink/app-wpf
dotnet publish -c Release
# 产物：bin/Release/net8.0-windows/win-x64/publish/PadLink.exe

# 安卓（本地无 SDK，走 GitHub Actions）
# 触发：POST /repos/Pusooy/padlink/actions/workflows/build-apk.yml/dispatches {"ref":"main"}
# 产物名：PadLink-apk → app-debug.apk
```

## 关键结论

- **纯单文件的关键**：前端资源必须用 `EmbeddedResource` 而不是 `None + CopyToOutputDirectory`，
  否则 wwwroot 会被复制到 exe 旁边。WPF 单文件发布不会把 EmbeddedResource 抽成外部文件。
- 运行时在 exe 同目录生成的 `apps.json`（自定义应用列表）和 `padlink.log`（运行日志）都是
  **可选的运行时文件，删掉也能正常运行**（apps.json 会自动重建默认项）。

相关：[[padlink-project]]、[[user-environment-facts]]
"""

io.open(P, "w", encoding="utf-8").write(content)
print("已写入 padlink-artifacts.md")

# 更新 MEMORY.md 索引
IDX = (r"C:\Users\trump\.zcode\cli\memories\projects"
       r"\40c7b29c-1556-4b3b-ad16-4ac9f6367c84-8b8417c80882ffc3\memory\MEMORY.md")
s = io.open(IDX, encoding="utf-8").read()
line = "- [PadLink 产物路径](padlink-artifacts.md) — 每次交付必须主动告知：exe / apk / PWA 位置\n"
if "padlink-artifacts" not in s:
    s = s.rstrip() + "\n" + line
    io.open(IDX, "w", encoding="utf-8").write(s)
    print("MEMORY.md 索引已更新")
else:
    print("索引已存在")
