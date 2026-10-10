"""Cuts the animation frames out of the character sheets (JPGs with lavender cells) and removes the background.
Usage (project root):  python tools/extract_character_frames.py [folder with Kris.jpg, Noelle.jpg, Ralsei.jpg, Susie.jpg]
Writes Assets/Menu/Characters/<Name>_<n>.png, all frames of a character padded to one common size
(bottom-centre aligned so the feet don't jitter)."""
import glob
import os
import sys
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.expanduser("~"), "Desktop")
OUT = os.path.join(ROOT, "Assets/Menu/Characters")
os.makedirs(OUT, exist_ok=True)
LAV = np.array([194, 141, 249], float)


def runs(flags):
    out, start = [], None
    for i, f in enumerate(flags):
        if f and start is None:
            start = i
        if not f and start is not None:
            out.append((start, i))
            start = None
    if start is not None:
        out.append((start, len(flags)))
    return out


def dist(a, c):
    return np.sqrt(((a.astype(float) - c) ** 2).sum(-1))


def components(mask):
    """4-connected components of a boolean mask -> list of index arrays (no scipy needed)."""
    h, w = mask.shape
    seen = np.zeros_like(mask, bool)
    comps = []
    for y in range(h):
        for x in range(w):
            if mask[y, x] and not seen[y, x]:
                stack, pts = [(y, x)], []
                seen[y, x] = True
                while stack:
                    cy, cx = stack.pop()
                    pts.append((cy, cx))
                    for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            stack.append((ny, nx))
                comps.append(pts)
    return comps


def extract(name):
    img = np.array(Image.open(os.path.join(SRC, name + ".jpg")).convert("RGB"))
    h, w, _ = img.shape
    near = dist(img, LAV) < 38
    # cells are separated by darker purple borders: find the border columns, cells are what lies between
    border = np.array([139, 90, 160], float)
    is_border = dist(img, border) < 45
    border_cols = is_border.mean(axis=0) > 0.6
    cells_x = [r for r in runs(~border_cols) if r[1] - r[0] > 10]
    widest = max(r[1] - r[0] for r in cells_x)
    cells_x = [r for r in cells_x if r[1] - r[0] > widest * 0.6]       # drop slivers at the sheet edges
    frames = []
    for x0, x1 in cells_x:
        border_rows = is_border[:, x0:x1].mean(axis=1) > 0.6
        ry = runs(~border_rows)
        y0, y1 = max(ry, key=lambda r: r[1] - r[0])
        cell = img[y0:y1, x0:x1]
        bg = np.median(cell[dist(cell, LAV) < 50], axis=0) if (dist(cell, LAV) < 50).any() else LAV
        # Background = pixels close to the cell colour that are connected to the cell edge (flood fill).
        # Anything enclosed by the sprite (e.g. a pale face highlight) stays opaque, whatever its colour.
        close = dist(cell, bg) <= 58
        hh, ww = close.shape
        outside = np.zeros_like(close)
        stack = [(y, x) for y in range(hh) for x in (0, ww - 1) if close[y, x]] +                 [(y, x) for x in range(ww) for y in (0, hh - 1) if close[y, x]]
        for (y, x) in stack:
            outside[y, x] = True
        while stack:
            cy, cx = stack.pop()
            for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                if 0 <= ny < hh and 0 <= nx < ww and close[ny, nx] and not outside[ny, nx]:
                    outside[ny, nx] = True
                    stack.append((ny, nx))
        fg = ~outside
        # drop JPEG speckle: keep blobs that are a meaningful share of the biggest one
        comps = components(fg)
        keep = np.zeros_like(fg)
        if comps:
            biggest = max(len(c) for c in comps)
            for c in comps:
                if len(c) >= max(6, biggest * 0.04):
                    for (yy, xx) in c:
                        keep[yy, xx] = True
        # trim the lavender halo JPEG leaves on the silhouette edge
        pad = np.pad(keep, 1)
        edge = keep & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
        keep &= ~(edge & (dist(cell, bg) < 85))
        ys, xs = np.where(keep)
        if len(ys) == 0:
            continue
        crop = cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        alpha = keep[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        rgba = np.dstack([crop, alpha * 255]).astype(np.uint8)
        frames.append(rgba)
    return frames


for name in ["Kris", "Noelle", "Ralsei", "Susie"]:
    for old in glob.glob(os.path.join(OUT, name + "_*.png")):      # clear frames from earlier runs
        os.remove(old)
    frames = extract(name)
    W = max(f.shape[1] for f in frames)
    H = max(f.shape[0] for f in frames)
    for i, f in enumerate(frames):
        canvas = np.zeros((H, W, 4), np.uint8)
        ox = (W - f.shape[1]) // 2
        oy = H - f.shape[0]
        canvas[oy:oy + f.shape[0], ox:ox + f.shape[1]] = f
        Image.fromarray(canvas, "RGBA").save(os.path.join(OUT, f"{name}_{i}.png"))
    print(name, len(frames), "frames", W, "x", H)

# contact sheet preview
tmp = os.environ.get("TEMP", ".")
rows = []
for name in ["Kris", "Noelle", "Ralsei", "Susie"]:
    files = sorted(glob.glob(os.path.join(OUT, name + "_*.png")), key=lambda p: int(p.rsplit("_", 1)[1][:-4]))
    ims = [Image.open(p) for p in files]
    rows.append(ims)
S = 4
width = max(sum(i.width * S + 8 for i in r) for r in rows)
height = sum(max(i.height for i in r) * S + 8 for r in rows)
sheet = Image.new("RGBA", (width, height), (76, 130, 200, 255))
y = 0
for r in rows:
    x = 0
    for i in r:
        big = i.resize((i.width * S, i.height * S), Image.NEAREST)
        sheet.alpha_composite(big, (x, y))
        x += big.width + 8
    y += max(i.height for i in r) * S + 8
sheet.save(os.path.join(tmp, "characters_preview.png"))
