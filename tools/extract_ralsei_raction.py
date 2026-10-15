"""Builds Ralsei's R-Action (wraps his scarf around himself and a hookable object, becoming a platform)
from tools/source_sheets/Ralsei_actions.png.

Sheet cells (first row, left to right): 1-6 Ralsei lying with the scarf wound around him, 7-9 the scarf standing up.
In the game those are layers: Ralsei and the scarf are on screen at the same time. So this script
  * draws a body frame and a scarf frame together in the same cell coordinates, and
  * stretches the scarf shaft down until it reaches his back (the original stops short), which makes it taller.

Run from the project root:  python tools/extract_ralsei_raction.py
Output: Assets/Characters/Ralsei/RAction/Ralsei_wrap_<n>.png  (+ a preview in %TEMP%)"""
import glob
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "tools/source_sheets/Ralsei_actions.png")
OUT = os.path.join(ROOT, "Assets/Characters/Ralsei/RAction")
BORDER = np.array([138, 91, 161], float)
LAV = np.array([195, 134, 255], float)

# play order; numbers are the sheet's cell numbers (first row, left to right):
#   ("body", n)               Ralsei lying with the scarf wound around him
#   ("both", body, scarf)     body + the scarf standing up from his back
#   ("full", body, scarf, head)  same, plus his head: cells 11-13 are a separate head sprite (in the game the head
#                             follows Kris), and body frame 6 has no head of its own
SEQUENCE = [("body", 1), ("body", 2), ("body", 3), ("both", 3, 7), ("full", 6, 8, 11), ("full", 6, 9, 11), ("full", 6, 9, 11)]
FACE_MIN_X = 30         # his white shoes sit left of this; only the face is used to place the head
TALL = 40               # how many pixels taller than the sheet the scarf stands (0 = original height)
EXTRA_TALL = 0


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
                    for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                        if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            stack.append((ny, nx))
                comps.append(pts)
    return comps


def cut_cell(sheet_row, cell, is_border):
    """RGBA of one cell with the lavender background removed; cell rows are bottom aligned later."""
    x0, x1 = cell
    border_rows = is_border[:, x0:x1].mean(axis=1) > 0.6
    y0, y1 = max(runs(~border_rows), key=lambda r: r[1] - r[0])
    img = sheet_row[y0:y1, x0:x1]
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
    comps = components(fg)
    keep = np.zeros_like(fg)
    if comps:
        biggest = max(len(c) for c in comps)
        for c in comps:
            if len(c) >= max(6, biggest * 0.02):
                for (yy, xx) in c:
                    keep[yy, xx] = True
    pad = np.pad(keep, 1)
    edge = keep & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    keep &= ~(edge & (dist(img, LAV) < 85))
    keep[0, :] = keep[-1, :] = False                       # cell-border leftovers in the corners
    keep[:, 0] = keep[:, -1] = False
    return np.dstack([img, keep * 255]).astype(np.uint8)


def to_canvas(rgba, W, H):
    c = np.zeros((H, W, 4), np.uint8)
    c[H - rgba.shape[0]:, :rgba.shape[1]] = rgba           # all cells share a bottom edge
    return c


def stretch_scarf(scarf, body):
    """Extend the scarf shaft downward until it reaches the top of the body, so it connects (and is taller)."""
    out = scarf.copy()
    rows = np.where((out[:, :, 3] > 0).sum(axis=1) >= 3)[0]                 # ignores stray pixels
    top, bottom = rows.min(), rows.max()
    clean = out[bottom - 8].copy()                           # a plain mid-shaft row: outline, pink, outline

    # 1. taller: lift the knot and upper shaft by TALL rows and fill the gap with clean shaft rows
    split = bottom - 10
    if TALL > 0:
        lifted = np.zeros_like(out)
        lifted[:split - TALL] = out[TALL:split]
        lifted[split - TALL:split] = clean
        lifted[split:] = out[split:]
        out = lifted
    out[split - TALL:bottom + 1] = clean                     # repaint the plain shaft: the source has a faint seam in it

    # 2. connect: run the shaft down until it meets the top of the body
    # (column by column, because the body's top edge under the shaft is not level)
    shaft_cols = np.where(clean[:, 3] > 0)[0]
    for x in shaft_cols:
        below = np.where(body[bottom + 1:, x, 3] > 0)[0]
        stop = bottom + 1 + (below.min() if len(below) else 0) + 3
        for y in range(bottom + 1, min(stop, out.shape[0])):
            out[y, x] = clean[x]
    return out


def over(base, top):
    """Alpha-composite two RGBA arrays (same size): `top` drawn over `base`."""
    a = Image.fromarray(base, "RGBA")
    a.alpha_composite(Image.fromarray(top, "RGBA"))
    return np.array(a)


sheet = np.array(Image.open(SRC).convert("RGB"))
row1 = sheet[:130]
is_border = dist(row1, BORDER) < 40
cells = [r for r in runs(~(is_border.mean(axis=0) > 0.7)) if r[1] - r[0] > 4]
needed = sorted({n for step in SEQUENCE for n in step[1:]})
cut = {n: cut_cell(row1, cells[n - 1], is_border) for n in needed}
W = max(c.shape[1] for c in cut.values())
H = max(c.shape[0] for c in cut.values())
canvas = {n: np.pad(to_canvas(c, W, H), ((TALL, 0), (0, 0), (0, 0))) for n, c in cut.items()}   # headroom for the taller scarf
H += TALL

def white_box(rgba, min_x=0):
    """Bounding box of the (near) white pixels = his face / fur. min_x skips the white shoes at the left end."""
    m = (rgba[:, :, 3] > 0) & (rgba[:, :, 0] > 225) & (rgba[:, :, 1] > 225) & (rgba[:, :, 2] > 225)
    m[:, :min_x] = False
    ys, xs = np.where(m)
    return xs.min(), xs.max(), ys.min(), ys.max()


def shifted(rgba, dx, dy):
    out = np.zeros_like(rgba)
    h, w = rgba.shape[:2]
    out[max(0, dy):min(h, h + dy), max(0, dx):min(w, w + dx)] = rgba[max(0, -dy):min(h, h - dy), max(0, -dx):min(w, w - dx)]
    return out


def head_layer(head_canvas, face_canvas):
    """Move the separate head sprite onto the neck: centre its white area on the face of a body frame that has a head."""
    fx0, fx1, fy0, fy1 = white_box(face_canvas, min_x=FACE_MIN_X)
    hx0, hx1, hy0, hy1 = white_box(head_canvas)
    return shifted(head_canvas, int(round((fx0 + fx1) / 2 - (hx0 + hx1) / 2)), int(round((fy0 + fy1) / 2 - (hy0 + hy1) / 2)))


face_reference = canvas[3]          # body frame 3 still has its own head: it tells us where the head belongs

frames = []
for step in SEQUENCE:
    if step[0] == "body":
        frames.append(canvas[step[1]])
    else:
        body, scarf = canvas[step[1]], canvas[step[2]]
        frame = over(body, stretch_scarf(scarf, body))                 # scarf behind...
        if step[0] == "full":
            frame = over(frame, head_layer(canvas[step[3]], face_reference))   # ...head in front of it
        frames.append(frame)

# shared crop box so every frame keeps its position
union = np.any([f[:, :, 3] > 0 for f in frames], axis=0)
ys, xs = np.where(union)
box = (xs.min(), max(0, ys.min() - EXTRA_TALL), xs.max() + 1, ys.max() + 1)

os.makedirs(OUT, exist_ok=True)
for old in glob.glob(os.path.join(OUT, "*.png")):
    os.remove(old)
saved = []
for i, f in enumerate(frames):
    img = Image.fromarray(f, "RGBA").crop(box)
    img.save(os.path.join(OUT, f"Ralsei_wrap_{i}.png"))
    saved.append(img)
print("frames", len(saved), "size", saved[0].size)

# The final pose again as two layers with the same canvas, for hanging him in the level: the body (with his head) is drawn
# in front of Kris so his feet are hidden while he stands on it, the scarf behind Kris.
hook_body = over(canvas[6], head_layer(canvas[11], face_reference))
hook_scarf = stretch_scarf(canvas[9], canvas[6])
for layer_name, layer in (("body", hook_body), ("scarf", hook_scarf)):
    Image.fromarray(layer, "RGBA").crop(box).save(os.path.join(OUT, f"Ralsei_hook_{layer_name}.png"))

S = 3
sheet_img = Image.new("RGBA", (len(saved) * (saved[0].width * S + 8) + 8, saved[0].height * S + 16), (76, 130, 200, 255))
x = 8
for img in saved:
    sheet_img.alpha_composite(img.resize((img.width * S, img.height * S), Image.NEAREST), (x, 8))
    x += img.width * S + 8
sheet_img.save(os.path.join(os.environ.get("TEMP", "."), "ralsei_wrap_preview.png"))
