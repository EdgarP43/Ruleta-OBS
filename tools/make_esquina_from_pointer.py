"""Crea michi-esquina.png usando el pelaje exacto de michi-pointer.png."""
from PIL import Image, ImageDraw, ImageFilter
import os
import math

root = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\src\RuletaObs.App\wwwroot"
src = Image.open(os.path.join(root, "michi-pointer.png")).convert("RGBA")
w, h = src.size
px = src.load()

# Quitar solo fondo morado (no el blanco del hocico/pecho)
out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
opx = out.load()
for y in range(h):
    for x in range(w):
        r, g, b, a = px[x, y]
        if a < 8:
            continue
        # blanco / crema del pelaje: conservar
        if r > 200 and g > 200 and b > 200:
            opx[x, y] = (r, g, b, 255)
            continue
        # rosa (orejas/nariz/lengua)
        if r > 160 and g < 160 and b < 160 and r > g and r > b:
            opx[x, y] = (r, g, b, a)
            continue
        # morado de fondo: r y b altos, g más bajo
        is_purple = (b > 90 and r > 70 and b >= g and (b - g) > 25 and (r + b) / 2 > g + 15)
        # saturación baja = gris/marrón del pelaje
        mx, mn = max(r, g, b), min(r, g, b)
        sat = (mx - mn) / (mx + 1e-6)
        if is_purple and sat > 0.12 and not (r > 170 and g > 140 and b < 120):
            # cerca de blanco no
            if not (r > 190 and g > 180 and b > 170):
                continue
        opx[x, y] = (r, g, b, 255)

# Recortar al contenido
bbox = out.getbbox()
cat = out.crop(bbox)
# Quitar letras blancas grandes de la derecha si quedaron: recortar centrando al gato
cw, ch = cat.size
# Preferir zona izquierda-centro donde está el gato
cat = cat.crop((0, 0, int(cw * 0.72), ch))
bbox2 = cat.getbbox()
if bbox2:
    cat = cat.crop(bbox2)

# Canvas circular
size = 1024
canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)
margin = 36
# círculo morado + borde dorado
draw.ellipse((margin, margin, size - margin, size - margin), fill=(124, 58, 237, 255))
draw.ellipse((margin, margin, size - margin, size - margin), outline=(253, 230, 138, 255), width=18)

# Encajar el gato
max_w = int(size * 0.78)
max_h = int(size * 0.78)
cat.thumbnail((max_w, max_h), Image.Resampling.LANCZOS)
cx = (size - cat.width) // 2
cy = (size - cat.height) // 2 + 20
canvas.paste(cat, (cx, cy), cat)

# Máscara circular final (fuera del borde transparente)
mask = Image.new("L", (size, size), 0)
md = ImageDraw.Draw(mask)
md.ellipse((margin - 2, margin - 2, size - margin + 2, size - margin + 2), fill=255)
canvas.putalpha(Image.composite(canvas.split()[-1], Image.new("L", (size, size), 0), mask))

# Asegurar blancos internos opacos
px2 = canvas.load()
# flood outside
from collections import deque
outside = [[False] * size for _ in range(size)]
q = deque()

def enq(x, y):
    if 0 <= x < size and 0 <= y < size and not outside[y][x]:
        a = px2[x, y][3]
        if a < 15:
            outside[y][x] = True
            q.append((x, y))

for x in range(size):
    enq(x, 0); enq(x, size - 1)
for y in range(size):
    enq(0, y); enq(size - 1, y)
while q:
    x, y = q.popleft()
    for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
        enq(nx, ny)

for y in range(size):
    for x in range(size):
        r, g, b, a = px2[x, y]
        if a < 15 and not outside[y][x]:
            px2[x, y] = (255, 255, 255, 255)
        elif a > 0 and r > 210 and g > 210 and b > 210:
            px2[x, y] = (max(r, 245), max(g, 245), max(b, 245), 255)

dst = os.path.join(root, "michi-esquina.png")
canvas.save(dst, "PNG")
print("saved", dst, canvas.size, os.path.getsize(dst))

# También arreglar flecha: forzar blancos internos
flecha_path = os.path.join(root, "michi-flecha.png")
if os.path.exists(flecha_path):
    f = Image.open(flecha_path).convert("RGBA")
    fw, fh = f.size
    fpx = f.load()
    outside = [[False] * fw for _ in range(fh)]
    q = deque()
    def enqf(x, y):
        if 0 <= x < fw and 0 <= y < fh and not outside[y][x] and fpx[x, y][3] < 20:
            outside[y][x] = True
            q.append((x, y))
    for x in range(fw):
        enqf(x, 0); enqf(x, fh - 1)
    for y in range(fh):
        enqf(0, y); enqf(fw - 1, y)
    while q:
        x, y = q.popleft()
        for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            enqf(nx, ny)
    n = 0
    for y in range(fh):
        for x in range(fw):
            r, g, b, a = fpx[x, y]
            if a < 20 and not outside[y][x]:
                fpx[x, y] = (255, 255, 255, 255); n += 1
            elif a > 0 and r > 210 and g > 210 and b > 210 and not outside[y][x]:
                fpx[x, y] = (255, 255, 255, 255)
    f.save(flecha_path, "PNG")
    print("flecha white-fixed", n)

print("done")
