from PIL import Image
from collections import deque
import os

src = r"C:\Users\epare\.cursor\projects\c-Users-epare-OneDrive-Documents-Ruleta-OBS\assets\michi-flecha.jpg"
dst = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\src\RuletaObs.App\wwwroot\michi-flecha.png"
assets = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\assets"

im = Image.open(src).convert("RGBA")
w, h = im.size
px = im.load()
out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
opx = out.load()

def keep(r, g, b):
    mx, mn = max(r, g, b), min(r, g, b)
    # fondo gris claro del JPG (~225-230)
    if mx - mn < 18 and 200 <= mn <= 245:
        return None
    # blanco puro del pelaje
    if mn > 245 and (mx - mn) < 20:
        return (255, 255, 255, 255)
    if mn > 235 and (mx - mn) < 25 and mx > 248:
        return (255, 255, 255, 255)
    # amarillo flecha
    if r > 170 and g > 130 and b < 130 and r >= g > b:
        return (r, g, b, 255)
    # marrón / tabby
    if 30 < r < 210 and g < r + 8 and b < g + 20 and r > b + 20:
        return (r, g, b, 255)
    # rosa
    if r > 150 and g < 160 and b < 160 and r > g + 20 and r > b + 20:
        return (r, g, b, 255)
    # ojos / negros suaves
    if mx < 100 and mx > 15:
        return (r, g, b, 255)
    return None

for y in range(h):
    for x in range(w):
        k = keep(*px[x, y][:3])
        if k:
            opx[x, y] = k

outside = [[False] * w for _ in range(h)]
q = deque()

def enq(x, y):
    if 0 <= x < w and 0 <= y < h and not outside[y][x] and opx[x, y][3] < 20:
        outside[y][x] = True
        q.append((x, y))

for x in range(w):
    enq(x, 0); enq(x, h - 1)
for y in range(h):
    enq(0, y); enq(w - 1, y)
while q:
    x, y = q.popleft()
    for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
        enq(nx, ny)

for y in range(h):
    for x in range(w):
        if opx[x, y][3] < 20 and not outside[y][x]:
            opx[x, y] = (255, 255, 255, 255)

bbox = out.getbbox()
if bbox:
    pad = 10
    out = out.crop((max(0, bbox[0] - pad), max(0, bbox[1] - pad), min(w, bbox[2] + pad), min(h, bbox[3] + pad)))

out.save(dst, "PNG")
os.makedirs(assets, exist_ok=True)
out.save(os.path.join(assets, "michi-flecha.png"), "PNG")
print("corner", out.getpixel((0, 0)), "size", out.size, "bytes", os.path.getsize(dst))
