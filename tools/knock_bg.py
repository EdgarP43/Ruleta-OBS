from PIL import Image
import os

def knock_by_corner(src, dst, soft=48, also_dark=True):
    im = Image.open(src).convert("RGBA")
    pixels = im.load()
    w, h = im.size
    samples = [pixels[2, 2], pixels[w - 3, 2], pixels[2, h - 3], pixels[w - 3, h - 3]]
    br = sum(p[0] for p in samples) / 4
    bg = sum(p[1] for p in samples) / 4
    bb = sum(p[2] for p in samples) / 4
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            dist = ((r - br) ** 2 + (g - bg) ** 2 + (b - bb) ** 2) ** 0.5
            lum = (r + g + b) / 3
            if dist < soft or (also_dark and lum < 16):
                pixels[x, y] = (r, g, b, 0)
            elif dist < soft * 1.7:
                alpha = int(255 * (dist - soft) / (soft * 0.7))
                pixels[x, y] = (r, g, b, min(a, max(0, alpha)))
    bbox = im.getbbox()
    if bbox:
        pad = 10
        im = im.crop((max(0, bbox[0] - pad), max(0, bbox[1] - pad), min(w, bbox[2] + pad), min(h, bbox[3] + pad)))
    im.save(dst, "PNG")
    print(dst, im.size, os.path.getsize(dst))

base = r"C:\Users\epare\.cursor\projects\c-Users-epare-OneDrive-Documents-Ruleta-OBS\assets"
out = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\src\RuletaObs.App\wwwroot"
assets = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\assets"
os.makedirs(assets, exist_ok=True)

# flecha: purple mat -> transparent
knock_by_corner(os.path.join(base, "michi-flecha.jpg"), os.path.join(out, "michi-flecha.png"), soft=50, also_dark=False)
# esquina: keep purple circle, only remove outer black/gray
knock_by_corner(os.path.join(base, "michi-esquina.jpg"), os.path.join(out, "michi-esquina.png"), soft=28, also_dark=True)

Image.open(os.path.join(out, "michi-flecha.png")).save(os.path.join(assets, "michi-flecha.png"))
Image.open(os.path.join(out, "michi-esquina.png")).save(os.path.join(assets, "michi-esquina.png"))
print("done")
