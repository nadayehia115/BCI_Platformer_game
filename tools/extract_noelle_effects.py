"""Cuts Noelle's ice effects out of tools/source_sheets/Noelle_effects.png (six pieces on a lavender background).

Run from the project root:  python tools/extract_noelle_effects.py
Output: Assets/Characters/Noelle/Effects/<name>.png  (+ a preview in %TEMP%\\noelle_effects_preview.png)

  fx_snow_a      scattered snow dots (small), a whole cell: use it as a patch of falling snow
  fx_disc        the white disc: the sheet draws it as a white square with a round hole, so it is inverted here
  fx_snow_b      scattered snow dots (big and small)
  fx_snowflake   the snowflake
  fx_patch       the frozen puddle
  fx_orb         the small striped ice orb
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "tools/source_sheets/Noelle_effects.png")
OUT = os.path.join(ROOT, "Assets/Characters/Noelle/Effects")
BORDER = np.array([139, 90, 156], float)
LAV = np.array([195, 134, 255], float)


def dist(a, c):
    return np.sqrt(((a.astype(float) - c) ** 2).sum(-1))


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


sheet = np.array(Image.open(SRC).convert("RGB"))
is_border = dist(sheet, BORDER) < 40
cells = [r for r in runs(~(is_border.mean(axis=0) > 0.7)) if r[1] - r[0] > 30]     # the 5 big cells
print("cells", cells)
# the little striped orb: the right-most piece, found by hand (its outline is as dark as the sheet's border colour)
orb_box = (546, 3, 591, 48)


INSET = 3        # the cell frames are 2-3 px of purple that fades into the lavender: leave them out


def cell_rgb(x0, x1, y0=None, y1=None):
    if y0 is None:
        rows = np.where(~(is_border[:, x0:x1].mean(axis=1) > 0.6))[0]
        y0, y1 = rows.min(), rows.max() + 1
    return sheet[y0 + INSET:y1 - INSET, x0 + INSET:x1 - INSET]


def transparent_lavender(img):
    keep = dist(img, LAV) > 55                      # every lavender pixel and the purple fringe around it is background
    return np.dstack([img, keep * 255]).astype(np.uint8)


def white_on_transparent(img):
    """The snow dots are white on lavender with a pink fringe: turn the fringe into a soft white edge instead."""
    full = dist(np.array([255, 255, 255]), LAV)
    alpha = np.clip(dist(img, LAV) / full, 0, 1)
    alpha = np.where(alpha < 0.12, 0, np.where(alpha > 0.8, 1, alpha))
    out = np.zeros(img.shape[:2] + (4,), np.uint8)
    out[:, :, :3] = 255
    out[:, :, 3] = (alpha * 255).astype(np.uint8)
    return out


def trim(rgba):
    ys, xs = np.where(rgba[:, :, 3] > 0)
    return rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


pieces = {}
pieces["fx_snow_a"] = white_on_transparent(cell_rgb(*cells[0]))
disc_src = cell_rgb(*cells[1])
disc = np.zeros(disc_src.shape[:2] + (4,), np.uint8)
inside = dist(disc_src, LAV) <= 28                  # the round hole of the sheet = the disc
disc[inside] = (255, 255, 255, 255)
pieces["fx_disc"] = trim(disc)
pieces["fx_snow_b"] = white_on_transparent(cell_rgb(*cells[2]))
pieces["fx_snowflake"] = trim(transparent_lavender(cell_rgb(*cells[3])))
pieces["fx_patch"] = trim(transparent_lavender(cell_rgb(*cells[4])))
ox0, oy0, ox1, oy1 = orb_box
orb_rgb = sheet[oy0 + 2:oy1 - 2, ox0 + 2:ox1 - 2]
# the orb's own stripes are lavender too, so only the background OUTSIDE its black outline is removed (flood fill from the edge)
bg = (dist(orb_rgb, LAV) <= 55) | (dist(orb_rgb, BORDER) < 70)
outside = np.zeros_like(bg)
h, w = bg.shape
stack = [(y, x) for y in range(h) for x in (0, w - 1) if bg[y, x]] + [(y, x) for x in range(w) for y in (0, h - 1) if bg[y, x]]
for y, x in stack:
    outside[y, x] = True
while stack:
    cy, cx = stack.pop()
    for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
        if 0 <= ny < h and 0 <= nx < w and bg[ny, nx] and not outside[ny, nx]:
            outside[ny, nx] = True
            stack.append((ny, nx))
orb = np.dstack([orb_rgb, (~outside) * 255]).astype(np.uint8)
pieces["fx_orb"] = trim(orb)

os.makedirs(OUT, exist_ok=True)
for name, arr in pieces.items():
    Image.fromarray(arr, "RGBA").save(os.path.join(OUT, name + ".png"))
    print(name, arr.shape[1], "x", arr.shape[0])

S = 3
W = sum(a.shape[1] * S + 8 for a in pieces.values()) + 8
H = max(a.shape[0] for a in pieces.values()) * S + 16
preview = Image.new("RGBA", (W, H), (60, 90, 150, 255))
x = 8
for arr in pieces.values():
    img = Image.fromarray(arr, "RGBA")
    preview.alpha_composite(img.resize((img.width * S, img.height * S), Image.NEAREST), (x, 8))
    x += img.width * S + 8
preview.save(os.path.join(os.environ.get("TEMP", "."), "noelle_effects_preview.png"))
