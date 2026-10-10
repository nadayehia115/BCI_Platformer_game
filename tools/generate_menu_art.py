"""Generates the main-menu background and UI sprites from the project's tile sheets.
Run from the project root:  python tools/generate_menu_art.py
Background is 486x270 (27x15 tiles of 18px); Unity scales it 4x with Point filtering."""
import os
from PIL import Image
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET = Image.open(os.path.join(ROOT, "Assets/tiles/tileArt/tilemap_packed.png")).convert("RGBA")
BGS = Image.open(os.path.join(ROOT, "Assets/tiles/tileArt/tilemap-backgrounds_packed.png")).convert("RGBA")
OUT = os.path.join(ROOT, "Assets/Menu/Sprites")
T = 18
COLS, ROWS = 27, 15
W, H = COLS * T, ROWS * T


def tile(i):
    r, c = divmod(i, 20)
    return SHEET.crop((c * T, r * T, c * T + T, r * T + T))


canvas = Image.new("RGBA", (W, H))

# ---- sky: pale sky from the backgrounds sheet, tiled along the horizon -------------
sky = BGS.crop((0, 0, 64, 72))
top = sky.getpixel((0, 0))
bottom = sky.getpixel((0, 71))
canvas.paste(top, (0, 0, W, H))
horizon_y = 96                            # cloud band sits mid-screen, like the reference
for x in range(0, W, 64):
    canvas.alpha_composite(sky, (x, horizon_y))
canvas.paste(bottom, (0, horizon_y + 72, W, H))


def put(i, col, row):
    """Place tile i at grid col, row (row 0 = bottom)."""
    canvas.alpha_composite(tile(i), (col * T, H - (row + 1) * T))


# ---- terrain ------------------------------------------------------------------------
h = [9, 9, 8, 7, 6, 5, 4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3]
assert len(h) == COLS


def height(c):
    return h[c] if 0 <= c < COLS else None


def tile_idx(c, row):
    left_open = c > 0 and h[c - 1] <= row
    right_open = c < COLS - 1 and h[c + 1] <= row
    if row == h[c] - 1:            # grass top: 0 single, 1 left, 2 mid, 3 right
        return 0 if (left_open and right_open) else 1 if left_open else 3 if right_open else 2
    # dirt: 120 single, 121 left, 122 mid, 123 right
    return 120 if (left_open and right_open) else 121 if left_open else 123 if right_open else 122


for c in range(COLS):
    for row in range(h[c]):
        idx = tile_idx(c, row)
        img = tile(idx)
        if row == h[c] - 1 and row > 0:
            # Grass tiles carry their own bottom outline; with dirt below it shows as a dark line,
            # so replace those two pixel rows with the top of the dirt tile underneath.
            img = img.copy()
            img.paste(tile(tile_idx(c, row - 1)).crop((0, 0, T, 2)), (0, T - 2))
        canvas.alpha_composite(img, (c * T, H - (row + 1) * T))

# ---- tree (right side) ------------------------------------------------------------------
tc = 23
base = h[tc]


def canopy(c0, c1, r0, r1):
    for r in range(r0, r1 + 1):
        for c in range(c0, c1 + 1):
            top_row, bot_row = r == r1, r == r0
            first, last = c == c0, c == c1
            base_i = 17 if top_row else 57 if bot_row else 37    # left-cap tile of each row group
            put(base_i + (0 if first else 2 if last else 1), c, r)


canopy(20, 24, base + 2, base + 5)
canopy(23, 26, base + 4, base + 6)
put(137, tc, base)       # trunk is drawn over the canopy so the fork blends in
put(117, tc, base + 1)
put(117, tc, base + 2)   # trunk reaches up into the canopy

# ---- props on the ground ----------------------------------------------------------------
props = [(88, 10), (124, 11), (128, 13), (125, 15), (68, 16), (127, 17), (126, 19), (106, 20), (129, 25)]
for idx, c in props:
    put(idx, c, h[c])
put(150, 8, h[8])      # door
put(130, 8, h[8] + 1)
put(105, 6, h[6])

canvas.save(os.path.join(OUT, "menu_background.png"))
big = canvas.resize((W * 2, H * 2), Image.NEAREST)
big.save(os.path.join(os.environ.get("TEMP", "."), "menu_bg_preview.png"))
print("background", canvas.size)


# ================= UI sprites =================================================================
def sample(img, x, y):
    return img.getpixel((x, y))[:3]

NAVY = sample(tile(2), 9, 0)       # outline colour used by the tile sheet
ORANGE = (244, 166, 55)   # gold of the item blocks in the tile sheet
CREAM = (253, 240, 208)
ORANGE_LIGHT = tuple(min(255, int(c * 1.18)) for c in ORANGE)
ORANGE_DARK = tuple(int(c * 0.78) for c in ORANGE)


def save(name, arr):
    Image.fromarray(arr.astype(np.uint8), "RGBA").save(os.path.join(OUT, name))


def blank(w, h_):
    return np.zeros((h_, w, 4), dtype=np.uint8)


def fill(a, x0, y0, x1, y1, col):
    a[y0:y1, x0:x1] = (*col, 255)


def rounded_block(w, h_, outline, face, light, dark, lip):
    a = blank(w, h_)
    fill(a, 0, 0, w, h_, outline)
    fill(a, 2, 2, w - 2, h_ - 2, face)
    fill(a, 2, 2, w - 2, 3, light)              # top highlight
    fill(a, 2, 2, 3, h_ - 2, light)             # left highlight
    fill(a, 2, h_ - 2 - lip, w - 2, h_ - 2, dark)   # bottom shade (gives the button depth)
    for (x, y) in [(0, 0), (w - 1, 0), (0, h_ - 1), (w - 1, h_ - 1)]:
        a[y, x] = (0, 0, 0, 0)                  # round the outer corners
    return a


save("button.png", rounded_block(24, 24, NAVY, ORANGE, ORANGE_LIGHT, ORANGE_DARK, 3))        # 9-slice, border 8
save("panel.png", rounded_block(24, 24, NAVY, CREAM, (255, 252, 240), (232, 214, 176), 2))    # 9-slice, border 8
save("window.png", rounded_block(24, 24, NAVY, (172, 222, 232), (205, 240, 246), (140, 196, 210), 2))   # 9-slice, border 8
save("slider_track.png", rounded_block(12, 12, NAVY, (92, 104, 140), (92, 104, 140), (70, 80, 112), 1))   # 9-slice, border 4
save("slider_fill.png", rounded_block(12, 12, NAVY, ORANGE, ORANGE_LIGHT, ORANGE_DARK, 1))                # 9-slice, border 4


def shape_icon(muted):
    mask = np.zeros((16, 16), bool)
    mask[6:10, 2:5] = True                                   # speaker body
    for i, x in enumerate(range(5, 9)):                      # cone
        mask[6 - (i + 1):10 + (i + 1), x] = True
    if not muted:
        mask[5:11, 11] = True                                # small wave
        mask[3:13, 13] = True                                # big wave
    cross = np.zeros((16, 16), bool)
    if muted:
        for i in range(5):
            cross[5 + i, 10 + i] = cross[9 - i, 10 + i] = True
        cross[5 + 5 - 1 + 1 - 1, 12] = True
    a = blank(16, 16)
    grown = np.zeros_like(mask)
    both = mask | cross
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            grown |= np.roll(np.roll(both, dy, 0), dx, 1)
    a[grown] = (*NAVY, 255)
    a[mask] = (255, 255, 255, 255)
    a[cross] = (226, 80, 70, 255)
    return a


save("speaker_on.png", shape_icon(False))
save("speaker_off.png", shape_icon(True))
print("ui sprites ok  navy", NAVY, "orange", ORANGE)
