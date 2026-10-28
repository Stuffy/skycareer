"""Draws the SkyCareer app icon: a climbing jet over a sky gradient with a golden career trail.
Writes SkyCareer.ico (16-256 px), a 256 px PNG for the window/favicon and a 1024 px preview."""
import math, sys, pathlib
from PIL import Image, ImageDraw, ImageFilter

out = pathlib.Path(sys.argv[1])
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# Sky gradient (deep navy at the top to bright blue at the bottom) inside a rounded square.
grad = Image.new("RGBA", (S, S))
top, bottom = (12, 38, 92), (46, 140, 230)
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / (S - 1)
    gd.line([(0, y), (S, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=210, fill=255)
img.paste(grad, (0, 0), mask)

# Golden trail: a rising arc from the lower left towards the aircraft.
trail = Image.new("RGBA", (S, S), (0, 0, 0, 0))
td = ImageDraw.Draw(trail)
pts = []
for i in range(60):
    t = i / 59
    x = 150 + t * 470
    y = 860 - (t ** 1.6) * 470
    pts.append((x, y))
for i in range(len(pts) - 1):
    w = int(14 + 46 * i / len(pts))
    td.line([pts[i], pts[i + 1]], fill=(255, 196, 61, 255), width=w)
    td.ellipse([pts[i][0] - w / 2, pts[i][1] - w / 2, pts[i][0] + w / 2, pts[i][1] + w / 2], fill=(255, 196, 61, 255))
img.alpha_composite(trail)

# Jet seen from above, nose up, in a 100x100 design box, then rotated 45° to climb up-right.
half = [(50, 4), (55, 13), (56.5, 36), (95, 58), (95, 66), (56.5, 55), (55.5, 77), (71, 88), (71, 94), (52, 90), (50, 93)]
shape = half + [(100 - x, y) for x, y in reversed(half[1:-1])]
cx, cy, scale, ang = 640, 420, 6.2, math.radians(45)
def place(p):
    x, y = (p[0] - 50) * scale, (p[1] - 50) * scale
    return (cx + x * math.cos(ang) - y * math.sin(ang), cy + x * math.sin(ang) + y * math.cos(ang))
plane = [place(p) for p in shape]

shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(shadow).polygon([(x + 14, y + 22) for x, y in plane], fill=(0, 20, 60, 120))
img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))
ImageDraw.Draw(img).polygon(plane, fill=(255, 255, 255, 255))

# Keep everything inside the rounded square.
clipped = Image.new("RGBA", (S, S), (0, 0, 0, 0))
clipped.paste(img, (0, 0), mask)

clipped.save(out / "icon-preview.png")
clipped.resize((256, 256), Image.Resampling.LANCZOS).save(out / "icon-256.png")
clipped.save(out / "SkyCareer.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("written to", out)
