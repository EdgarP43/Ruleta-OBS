"""Rellena agujeros transparentes internos con blanco opaco (hocico/pecho/patas)."""
from PIL import Image
from collections import deque
import os

def fill_internal_holes_white(src, dst, alpha_cut=20):
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    px = im.load()

    # Marca "afuera": transparente alcanzable desde los bordes
    outside = [[False] * w for _ in range(h)]
    q = deque()

    def enqueue(x, y):
        if 0 <= x < w and 0 <= y < h and not outside[y][x] and px[x, y][3] <= alpha_cut:
            outside[y][x] = True
            q.append((x, y))

    for x in range(w):
        enqueue(x, 0)
        enqueue(x, h - 1)
    for y in range(h):
        enqueue(0, y)
        enqueue(w - 1, y)

    while q:
        x, y = q.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            enqueue(nx, ny)

    filled = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a <= alpha_cut and not outside[y][x]:
                px[x, y] = (255, 255, 255, 255)
                filled += 1
            elif 0 < a < 220 and not outside[y][x]:
                # semi-transparente interno: mezcla hacia blanco opaco
                t = a / 255.0
                px[x, y] = (
                    int(r * t + 255 * (1 - t)),
                    int(g * t + 255 * (1 - t)),
                    int(b * t + 255 * (1 - t)),
                    255,
                )
                filled += 1

    im.save(dst, "PNG")
    print(f"{os.path.basename(dst)}: filled {filled} px -> {dst}")

root = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\src\RuletaObs.App\wwwroot"
for name in ("michi-flecha.png", "michi-esquina.png"):
    path = os.path.join(root, name)
    fill_internal_holes_white(path, path)

assets = r"c:\Users\epare\OneDrive\Documents\Ruleta OBS\assets"
os.makedirs(assets, exist_ok=True)
for name in ("michi-flecha.png", "michi-esquina.png"):
    Image.open(os.path.join(root, name)).save(os.path.join(assets, name))
print("done")
