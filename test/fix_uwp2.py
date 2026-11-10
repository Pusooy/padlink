# -*- coding: utf-8 -*-
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\VolumeService.cs"
s = io.open(P, encoding="utf-8").read()

aumids = [
    "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
    "Microsoft.Paint_8wekyb3d8bbwe!App",
    "Microsoft.ScreenSketch_8wekyb3d8bbwe!App",
    "Microsoft.Windows.Photos_8wekyb3d8bbwe!App",
    "Microsoft.WindowsCamera_8wekyb3d8bbwe!App",
    "Microsoft.WindowsAlarms_8wekyb3d8bbwe!App",
    "Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App",
    "Microsoft.WindowsStore_8wekyb3d8bbwe!App",
]

PREFIX = "shell:AppsFolder" + chr(92)  # 单个反斜杠

for a in aumids:
    old = '@"' + a + '"'
    new = '@"' + PREFIX + a + '"'
    if old in s:
        s = s.replace(old, new)

io.open(P, "w", encoding="utf-8").write(s)

for line in s.split("\n"):
    if "AppsFolder" in line and "summary" not in line and "//" not in line:
        print(line.strip())
