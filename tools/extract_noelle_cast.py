"""Cuts Noelle's spell-casting frames out of tools/source_sheets/Noelle_cast.png (18 cells on a lavender background).

Run from the project root:  python tools/extract_noelle_cast.py
Output: Assets/Characters/Noelle/Cast/Noelle_cast_<n>.png  (one common canvas, feet on the bottom row, same size for every frame)
        + a preview in %TEMP%\\noelle_cast_preview.png

Frames, left to right: 1-5 hands together, 6-7 kneeling, 8-14 spell with sparkles, 15-18 release.
"""
import glob
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "tools/source_sheets/Noelle_cast.png")
OUT = os.path.join(ROOT, "Assets/Characters/Noelle/Cast")
BORDER = np.array([139, 90, 156], float)
LAV = np.array([195, 134, 255], float)
MIN_PIECE = 4          # keeps the little sparkle stars (the Ralsei sheet's cleanup would drop them)


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
                    for ny in (cy - 1, cy, cy + 1):
                        for nx in (cx - 1, cx, cx + 1):          # 8 neighbours: a sparkle's points count as one piece
                            if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                                seen[ny, nx] = True
                                stack.append((ny, nx))
                comps.append(pts)
    return comps


def cut_cell(sheet, cell, is_border):
    """RGBA of one cell with the lavender background removed."""
    x0, x1 = cell
    border_rows = is_border[:, x0:x1].mean(axis=1) > 0.6
    y0, y1 = max(runs(~border_rows), key=lambda r: r[1] - r[0])
    img = sheet[y0:y1, x0:x1]
    close = dist(img, LAV) <= 60
    hh, ww = close.shape
    outside = np.zeros_like(close)
    stack = [(y, x) for y in range(hh) for x in (0, ww - 1) if close[y, x]] + \
            [(y, x) for x in range(ww) for y in (0, hh - 1) if close[y, x]]
    for (y, x) in stack:
        outside[y, x] = True
    while stack:
        cy, cx = stack.pop()
        for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
            if 0 <= ny < hh and 0 <= nx < ww and close[ny, nx] and not outside[ny, nx]:
                outside[ny, nx] = True
                stack.append((ny, nx))
    fg = ~outside
    # lavender pixels that are not connected to the border (holes, gaps between the sparkle's points) are background too
    fg &= dist(img, LAV) > 25
    comps = components(fg)
    keep = np.zeros_like(fg)
    for c in comps:
        if len(c) >= MIN_PIECE:
            for (yy, xx) in c:
                keep[yy, xx] = True
    keep[0, :] = keep[-1, :] = False                       # cell-border leftovers in the corners
    keep[:, 0] = keep[:, -1] = False
    return np.dstack([img, keep * 255]).astype(np.uint8)


sheet = np.array(Image.open(SRC).convert("RGB"))
is_border = dist(sheet, BORDER) < 40
cells = [r for r in runs(~(is_border.mean(axis=0) > 0.7)) if r[1] - r[0] > 4]
cuts = [cut_cell(sheet, c, is_border) for c in cells]
print("cells", len(cuts), [c.shape[:2] for c in cuts][:4], "...")


def body_box(rgba):
    ys, xs = np.where(rgba[:, :, 3] > 0)
    return xs.min(), xs.max(), ys.min(), ys.max()


# one canvas for all frames: the feet of every frame on the bottom row, the body horizontally centred on the feet-row middle
W = max(c.shape[1] for c in cuts) + 8
H = max(c.shape[0] for c in cuts)
frames = []
for c in cuts:
    x0, x1, y0, y1 = body_box(c)
    feet = c[y1 - 3:y1 + 1, :, 3] > 0
    fx = np.where(feet.any(axis=0))[0]
    centre = (fx.min() + fx.max()) / 2.0                   # middle of the feet
    canvas = np.zeros((H, W, 4), np.uint8)
    dx = int(round(W / 2 - centre))
    dy = H - 1 - y1                                        # feet on the bottom row
    ys0, ys1 = max(0, dy), min(H, dy + c.shape[0])
    xs0, xs1 = max(0, dx), min(W, dx + c.shape[1])
    canvas[ys0:ys1, xs0:xs1] = c[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
    frames.append(canvas)

os.makedirs(OUT, exist_ok=True)
for old in glob.glob(os.path.join(OUT, "*.png")):
    os.remove(old)
for i, f in enumerate(frames):
    Image.fromarray(f, "RGBA").save(os.path.join(OUT, f"Noelle_cast_{i}.png"))
print("saved", len(frames), "frames, canvas", W, "x", H)

S = 3
sheet_img = Image.new("RGBA", (len(frames) * (W * S + 6) + 6, H * S + 12), (76, 130, 200, 255))
x = 6
for f in frames:
    sheet_img.alpha_composite(Image.fromarray(f, "RGBA").resize((W * S, H * S), Image.NEAREST), (x, 6))
    x += W * S + 6
sheet_img.save(os.path.join(os.environ.get("TEMP", "."), "noelle_cast_preview.png"))
