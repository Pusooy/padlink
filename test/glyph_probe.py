# -*- coding: utf-8 -*-
"""渲染候选 Fluent 字形码点，找出哪些在目标机器上真实存在（非空白）。"""
import io
import os

OUT = r"C:\Users\trump\AppData\Local\Temp\glyphtest"
os.makedirs(OUT, exist_ok=True)

candidates = {
    "Pointer_E962": 0xE962,
    "Touch_E7C9": 0xE7C9,
    "Mouse_E8FD": 0xE8FD,
    "Point_E8FB": 0xE8FB,
    "Hand_E8B8": 0xE8B8,
    "Cursor_E1C2": 0xE1C2,
    "Keyboard_E765": 0xE765,
    "Play_E768": 0xE768,
    "Apps_E71D": 0xE71D,
    "Screen_E7F4": 0xE7F4,
    "Folder_E8B7": 0xE8B7,
    "Copy_E8C8": 0xE8C8,
    "Power_E7E8": 0xE7E8,
    "Home_E80F": 0xE80F,
    "Settings_E713": 0xE713,
    "Add_E710": 0xE710,
    "Save_E74E": 0xE74E,
    "Delete_E74D": 0xE74D,
    "Rocket_E82D": 0xE82D,
    "QRCode_ED14": 0xED14,
    "Link_E71B": 0xE71B,
    "Refresh_E72C": 0xE72C,
}

xaml_items = []
for name, cp in candidates.items():
    xaml_items.append(
        f'<TextBlock x:Name="{name}" Text="&#x{cp:04X};" '
        f'FontFamily="Segoe Fluent Icons" FontSize="28" Margin="0,4,18,4"/>'
    )

xaml = f'''<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <TextBlock Text="Segoe Fluent Icons glyph test" FontSize="13" Margin="0,0,0,10"/>
  <StackPanel Orientation="Horizontal">
    {"".join(xaml_items)}
  </StackPanel>
  <TextBlock Text="order: {" | ".join(candidates.keys())}" FontSize="10" Margin="0,10,0,0" TextWrapping="Wrap" MaxWidth="1200"/>
</StackPanel>'''

ps = r'''
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
[xml]$xamlXml = @"
''' + xaml + r'''
"@
$reader = New-Object System.Xml.XmlNodeReader $xamlXml
$panel = [Windows.Markup.XamlReader]::Load($reader)
$panel.Background = [System.Windows.Media.Brushes]::White
$panel.Measure([System.Windows.Size]::new(1400, 400))
$panel.Arrange([System.Windows.Rect]::new(0, 0, 1400, 400))
$panel.UpdateLayout()

$rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(1400, 260, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($panel)
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$fs = [System.IO.File]::Create("C:\Users\trump\AppData\Local\Temp\glyphtest\glyphs.png")
$enc.Save($fs)
$fs.Close()
Write-Host "rendered"
'''

ps_path = os.path.join(OUT, "render.ps1")
with io.open(ps_path, "w", encoding="utf-8-sig") as f:
    f.write(ps)

import subprocess
r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps_path],
                   capture_output=True, text=True, errors="replace")
print(r.stdout.strip())
print(r.stderr.strip()[:500])
print("输出:", os.path.join(OUT, "glyphs.png"))
