"""Convierte JPG de esquina a PNG: solo hace transparente fuera del círculo. No toca el pelaje blanco."""
from PIL import Image, ImageDraw
import os

src = r"C:\Users\epare\.cursor\projects\c-Users-epare-OneDrive-Documents-Ruleta-OBS\assets\michi-esquina.jpg"
dst = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\src\RuletaObs.App\wwwroot\michi-esquina.png"
assets = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\assets"

im = Image.open(src).convert("RGBA")
w, h = im.size
# Detectar círculo: muestrear desde el centro hasta encontrar borde dorado/oscuro
cx, cy = w // 2, h // 2
px = im.load()
# radio: desde el borde hacia adentro, el fondo negro/oscuro externo
radius = min(cx, cy) - 4
mask = Image.new("L", (w, h), 0)
draw = ImageDraw.Draw(mask)
# un poco hacia adentro para limpiar esquinas
pad = int(min(w, h) * 0.02)
draw.ellipse((pad, pad, w - 1 - pad, h - 1 - pad), fill=255)

# Forzar blancos del pelaje a blanco opaco DENTRO del círculo
out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
opx = out.load()
mpx = mask.load()
for y in range(h):
    for x in range(w):
        if mpx[x, y] < 128:
            continue
        r, g, b, a = px[x, y]
        # blancos/cremas del pecho y hocico → blanco sólido
        if r > 200 and g > 195 and b > 185:
            opx[x, y] = (255, 255, 255, 255)
        else:
            opx[x, y] = (r, g, b, 255)

out.save(dst, "PNG")
os.makedirs(assets, exist_ok=True)
out.save(os.path.join(assets, "michi-esquina.png"), "PNG")
print("saved", dst, out.size, os.path.getsize(dst))
