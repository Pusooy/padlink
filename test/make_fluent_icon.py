# -*- coding: utf-8 -*-
"""
生成 Fluent Design 风格应用图标：
- 圆角方形渐变底（Fluent 标志性的柔和蓝）
- 白色鼠标指针（矢量绘制，几何精确）
- 多层抗锯齿，输出 16-256 全尺寸 ico
"""
import math
from PIL import Image, ImageDraw, ImageFilter

S = 1024  # 高分辨率绘制后缩放，保证小尺寸清晰
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# ---------- 1. 圆角矩形渐变背景 ----------
radius = int(S * 0.22)
bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bd = ImageDraw.Draw(bg)
bd.rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=(255, 255, 255, 255))

# 渐变：#4C86FF（上）→ #2F5FE0（下），Fluent 蓝
grad = Image.new("RGB", (1, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    # 轻微加亮左上、加深右下，模拟 Fluent 的光源
    r = int(76 + (47 - 76) * t)
    g = int(134 + (95 - 134) * t)
    b = int(255 + (224 - 255) * t)
    gd.point((0, y), fill=(r, g, b))
grad = grad.resize((S, S), Image.BILINEAR)

# 用圆角矩形做遮罩
grad_rgba = grad.convert("RGBA")
grad_rgba.putalpha(bg.split()[3])
img = Image.alpha_composite(img, grad_rgba)

# 顶部高光（Fluent 的柔和内发光）
highlight = Image.new("RGBA", (S, S), (0, 0, 0, 0))
hd = ImageDraw.Draw(highlight)
hd.ellipse([-S * 0.25, -S * 0.75, S * 1.25, S * 0.45], fill=(255, 255, 255, 34))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
img = Image.composite(Image.alpha_composite(img, highlight), img, mask)

draw = ImageDraw.Draw(img)

# ---------- 2. 鼠标指针（Fluent 风格的箭头） ----------
# 指针轮廓（归一化坐标，0..1），精确的 Windows 指针形状
pts = [
    (0.330, 0.215),   # 尖端
    (0.330, 0.700),   # 左侧竖直边
    (0.430, 0.600),   # 内侧凹角
    (0.500, 0.790),   # 尾巴左下
    (0.585, 0.752),   # 尾巴右下
    (0.515, 0.565),   # 内侧上收
    (0.645, 0.552),   # 右侧尖角
]
poly = [(x * S, y * S) for (x, y) in pts]

# 阴影（偏移下沉，柔化）
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(shadow)
sd.polygon([(x + S * 0.012, y + S * 0.016) for (x, y) in poly],
           fill=(12, 30, 80, 110))
shadow = shadow.filter(ImageFilter.GaussianBlur(S * 0.012))
img = Image.alpha_composite(img, shadow)

# 白色指针主体（带极细描边提升小尺寸辨识）
draw = ImageDraw.Draw(img)
draw.polygon(poly, fill=(255, 255, 255, 255), outline=(235, 242, 255, 255), width=max(2, S // 320))

# 指针内部的浅蓝高光（Fluent 层次感）
inner = [(x * S, y * S) for (x, y) in [
    (0.362, 0.285), (0.362, 0.640), (0.432, 0.560), (0.500, 0.748),
    (0.545, 0.726), (0.478, 0.535), (0.588, 0.524),
]]
draw.polygon(inner, fill=(228, 240, 255, 210))

# ---------- 3. 底部三段信号条（暗示"操控/连接"） ----------
bar_y = 0.855 * S
bar_h = 0.052 * S
bar_radius = bar_h / 2
widths = [0.085, 0.135, 0.185]
start_x = 0.5 * S - (sum(w * S for w in widths) + 0.028 * S * 2) / 2
x = start_x
alphas = [150, 200, 255]
for w, a in zip(widths, alphas):
    draw.rounded_rectangle([x, bar_y, x + w * S, bar_y + bar_h],
                           radius=bar_radius, fill=(255, 255, 255, a))
    x += w * S + 0.028 * S

# ---------- 4. 导出 ----------
out = img.resize((256, 256), Image.LANCZOS)
out.save(r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\app.ico",
         sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
out.save(r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\wwwroot\icon.png")

# 预览大图
img.resize((512, 512), Image.LANCZOS).save(
    r"C:\Users\trump\Downloads\sidestore-setup\fluent-icon-preview.png")
print("Fluent 图标已生成：app.ico (16-256) + icon.png (PWA) + 预览图")
