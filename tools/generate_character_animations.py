"""Builds run / jump / fall frames for Noelle, Ralsei and Susie from their idle frames
(Assets/Menu/Characters/<Name>_<n>.png, made by extract_character_frames.py).

The sheets only contain an idle loop, so the extra motion is made with pixel-safe tricks:
  * bounce   - whole-pixel lift of the sprite
  * lean     - shifting the upper rows sideways by whole pixels (a shear)
  * squash / stretch - removing or repeating whole rows, feet stay planted
Everything is nearest-neighbour, so the pixel art stays crisp.

Run from the project root:  python tools/generate_character_animations.py
Output: Assets/Characters/<Name>/<Name>_<idle|run|jump|fall>_<n>.png  (+ a preview in %TEMP%)"""
import glob
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Assets/Menu/Characters")
OUT = os.path.join(ROOT, "Assets/Characters")
NAMES = ["Noelle", "Ralsei", "Susie"]
PAD_X, PAD_TOP, PAD_BOTTOM = 6, 8, 3


def load_frames(name):
    files = sorted(glob.glob(os.path.join(SRC, name + "_*.png")), key=lambda p: int(p.rsplit("_", 1)[1][:-4]))
    return [np.array(Image.open(f).convert("RGBA")) for f in files]


TOP, SIDE = PAD_TOP, PAD_X        # set per character in build(): room for the stretch and lean


def pad(a):
    return np.pad(a, ((TOP, PAD_BOTTOM), (SIDE, SIDE), (0, 0)))


def resize_rows(a, delta):
    """Make the sprite `delta` rows taller (positive, repeats evenly spaced rows) or shorter (negative, drops them).
    The bottom edge stays where it is, so the feet don't move."""
    if delta == 0:
        return a
    rows = np.where(a[:, :, 3].any(axis=1))[0]
    top, bottom = rows.min(), rows.max() + 1
    body = a[top:bottom]
    h = body.shape[0]
    new_h = max(4, h + delta)
    idx = np.minimum((np.arange(new_h) * h / new_h).astype(int), h - 1)
    stretched = body[idx]
    out = np.zeros_like(a)
    out[bottom - new_h:bottom] = stretched if bottom - new_h >= 0 else stretched[new_h - bottom:]
    return out


def shift(a, dx=0, dy=0):
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    ys, xs = slice(max(0, dy), min(h, h + dy)), slice(max(0, dx), min(w, w + dx))
    yd, xd = slice(max(0, -dy), min(h, h - dy)), slice(max(0, -dx), min(w, w - dx))
    out[ys, xs] = a[yd, xd]
    return out


def lean(a, amount):
    """Shear: rows higher up are shifted sideways by up to `amount` whole pixels (positive = lean right)."""
    if amount == 0:
        return a
    rows = np.where(a[:, :, 3].any(axis=1))[0]
    top, bottom = rows.min(), rows.max() + 1
    out = np.zeros_like(a)
    for y in range(top, bottom):
        t = (bottom - 1 - y) / max(1, (bottom - 1 - top))          # 0 at the feet, 1 at the head
        dx = int(round(t * amount))
        out[y] = np.roll(a[y], dx, axis=0)
        if dx > 0:
            out[y, :dx] = 0
        elif dx < 0:
            out[y, dx:] = 0
    return out


def step_feet(a, left_up, right_up, foot_rows, forward):
    """Running legs: the bottom `foot_rows` rows are the feet. Each half of them (left / right of the feet's middle) is
    lifted by its own amount, and a lifted foot also swings `forward` pixels, so the feet take turns stepping
    while the other one stays planted on the ground."""
    rows = np.where(a[:, :, 3].any(axis=1))[0]
    bottom = rows.max() + 1
    feet = a[bottom - foot_rows:bottom]
    cols = np.where(feet[:, :, 3].any(axis=0))[0]
    mid = (cols.min() + cols.max() + 1) // 2
    out = a.copy()
    out[bottom - foot_rows:bottom] = 0
    h, w = a.shape[:2]
    for lo, hi, up in ((0, mid, left_up), (mid, w, right_up)):
        piece = np.zeros_like(a)
        piece[bottom - foot_rows:bottom, lo:hi] = a[bottom - foot_rows:bottom, lo:hi]
        if up:
            piece = shift(piece, forward, -up)
        mask = piece[:, :, 3] > 0
        out[mask] = piece[mask]
    return out


def make(base, stretch=0, lean_px=0, lift=0, feet=None):
    a = pad(base)
    a = resize_rows(a, stretch)
    a = lean(a, lean_px)
    if feet:
        a = step_feet(a, *feet)
    return shift(a, 0, -lift)


def build(name):
    global TOP, SIDE
    idle = load_frames(name)
    n = len(idle)
    anims = {}

    # motion is scaled with the sprite's size (Noelle's art is ~2x the resolution of the others)
    body = np.where(idle[0][:, :, 3].any(axis=1))[0]
    k = max(1, round((body.max() - body.min() + 1) / 50))
    TOP, SIDE = PAD_TOP + 10 * k, PAD_X + 4 * k
    anims["idle"] = [pad(f) for f in idle]

    # run: 8 frames, leaning into the direction of travel; the left foot steps in frames 1-3, the right one in 5-7.
    # The body only bobs a little (a bigger bounce lifted both feet off the ground, so it did not look like running).
    lift = [0, 1, 1, 1, 0, 1, 1, 1]
    stretch = [0] * 8            # no squash/stretch: the body stays as tall as when standing, so stopping does not change his size
    lean_px = [2] * 8
    left_up = [0, 3, 5, 3, 0, 0, 0, 0]
    right_up = [0, 0, 0, 0, 0, 3, 5, 3]
    foot_rows, forward = 6 * k, 2 * k
    anims["run"] = [make(idle[i % n], stretch[i] * k, lean_px[i] * k, lift[i] * k,
                         (left_up[i] * k, right_up[i] * k, foot_rows, forward)) for i in range(8)]

    # jump: stretch upward, no lift (the collider leaves the ground by itself)
    anims["jump"] = [make(idle[i % n], s * k, 0, 0) for i, s in enumerate([-2, 6, 9])]

    # fall: stretched, leaning back
    anims["fall"] = [make(idle[i % n], s * k, -3 * k, 0) for i, s in enumerate([6, 8, 8])]

    # one common canvas per character so every frame lines up
    H = max(f.shape[0] for fr in anims.values() for f in fr)
    W = max(f.shape[1] for fr in anims.values() for f in fr)
    folder = os.path.join(OUT, name)
    os.makedirs(folder, exist_ok=True)
    for old in glob.glob(os.path.join(folder, "*.png")):
        os.remove(old)
    for kind, frames in anims.items():
        for i, f in enumerate(frames):
            canvas = np.zeros((H, W, 4), np.uint8)
            canvas[H - f.shape[0]:, (W - f.shape[1]) // 2:(W - f.shape[1]) // 2 + f.shape[1]] = f
            Image.fromarray(canvas, "RGBA").save(os.path.join(folder, f"{name}_{kind}_{i}.png"))
    print(name, {k: len(v) for k, v in anims.items()}, "canvas", W, "x", H)
    return anims, (W, H)


if __name__ == "__main__":
    previews = []
    for nm in NAMES:
        previews.append(build(nm))
    # contact sheet: one row per animation, one block per character
    S = 3
    rows_total = sum(len(a) for a, _ in previews)
    tmp = os.environ.get("TEMP", ".")
    for nm, (anims, (W, H)) in zip(NAMES, previews):
        sheet = Image.new("RGBA", (max(len(f) for f in anims.values()) * (W * S + 8) + 8, 4 * (H * S + 8) + 8), (76, 130, 200, 255))
        y = 8
        for kind in ["idle", "run", "jump", "fall"]:
            x = 8
            for f in anims[kind]:
                cw = Image.new("RGBA", (W, H))
                cw.paste(Image.fromarray(np.pad(f, ((H - f.shape[0], 0), ((W - f.shape[1]) // 2, W - f.shape[1] - (W - f.shape[1]) // 2), (0, 0))), "RGBA"))
                sheet.alpha_composite(cw.resize((W * S, H * S), Image.NEAREST), (x, y))
                x += W * S + 8
            y += H * S + 8
        sheet.save(os.path.join(tmp, f"anim_{nm}.png"))
