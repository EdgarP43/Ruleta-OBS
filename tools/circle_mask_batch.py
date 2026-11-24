from PIL import Image, ImageDraw
import os

base = r"C:\Users\epare\.cursor\projects\c-Users-epare-OneDrive-Documents-Ruleta-OBS\assets"
out_dir = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\src\RuletaObs.App\wwwroot"
assets = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\assets"
os.makedirs(assets, exist_ok=True)

names = [
    "michi-esquina-dormido",
    "michi-esquina-curioso",
    "michi-esquina-lana",
]

for name in names:
    src = os.path.join(base, name + ".jpg")
    if not os.path.exists(src):
        src = os.path.join(base, name + ".png")
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    px = im.load()
    mask = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(mask)
    pad = int(min(w, h) * 0.02)
    draw.ellipse((pad, pad, w - 1 - pad, h - 1 - pad), fill=255)
    mpx = mask.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    opx = out.load()
    for y in range(h):
        for x in range(w):
            if mpx[x, y] < 128:
                continue
            r, g, b, a = px[x, y]
            if r > 200 and g > 195 and b > 185:
                opx[x, y] = (255, 255, 255, 255)
            else:
                opx[x, y] = (r, g, b, 255)
    dst = os.path.join(out_dir, name + ".png")
    out.save(dst, "PNG")
    out.save(os.path.join(assets, name + ".png"), "PNG")
    print("saved", dst, out.size, os.path.getsize(dst))

print("done")
