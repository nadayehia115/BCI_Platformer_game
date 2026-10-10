"""Synthesises placeholder menu audio (chiptune loop + UI blips).
Run from the project root:  python tools/generate_menu_audio.py
Replace the files in Assets/Menu/Audio with real ones any time; the names don't matter,
just reassign the clips on the AudioManager."""
import os
import wave
import numpy as np

SR = 22050
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets/Menu/Audio")
os.makedirs(OUT, exist_ok=True)


def write(name, data):
    data = np.clip(data, -1, 1)
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((data * 32767).astype("<i2").tobytes())


def square(freq, n, duty=0.5):
    t = np.arange(n) / SR
    return np.where((t * freq) % 1.0 < duty, 1.0, -1.0)


def tri(freq, n):
    t = np.arange(n) / SR
    return 2 * np.abs(2 * ((t * freq) % 1.0) - 1) - 1


def env(n, attack=0.005, release=0.08):
    e = np.ones(n)
    a, r = int(attack * SR), int(release * SR)
    e[:a] = np.linspace(0, 1, a)
    e[-r:] = np.minimum(e[-r:], np.linspace(1, 0, r))
    return e


def hz(note):                       # note = semitones from A4
    return 440.0 * 2 ** (note / 12)


# ---- music: 8 bars of 4/4 at 120 bpm (16 s), C major, loops seamlessly ---------------
beat = 60 / 120
step = beat / 2                     # eighth notes
C, D, E, F, G, A, B = -9, -7, -5, -4, -2, 0, 2
melody = [  # (note, eighths) ; None = rest
    (E + 12, 1), (G + 12, 1), (C + 24, 2), (G + 12, 1), (E + 12, 1), (G + 12, 2),
    (D + 12, 1), (F + 12, 1), (B + 12, 2), (F + 12, 1), (D + 12, 1), (F + 12, 2),
    (C + 12, 1), (E + 12, 1), (G + 12, 2), (E + 12, 1), (C + 12, 1), (E + 12, 2),
    (D + 12, 1), (E + 12, 1), (F + 12, 1), (G + 12, 1), (G + 12, 4),
    (A + 12, 1), (C + 24, 1), (E + 24, 2), (C + 24, 1), (A + 12, 1), (C + 24, 2),
    (G + 12, 1), (B + 12, 1), (D + 24, 2), (B + 12, 1), (G + 12, 1), (B + 12, 2),
    (E + 12, 1), (G + 12, 1), (C + 24, 1), (B + 12, 1), (A + 12, 1), (G + 12, 1), (E + 12, 2),
    (D + 12, 2), (G + 12, 2), (C + 12, 4),
]
bass_roots = [C, F, C, G, A, G, C, G]       # one per bar
total = int(16 * SR)
lead = np.zeros(total)
pos = 0
for note, eighths in melody:
    n = int(eighths * step * SR)
    if pos + n > total:
        n = total - pos
    lead[pos:pos + n] += 0.16 * square(hz(note), n, 0.25) * env(n)
    pos += n
bass = np.zeros(total)
bar = int(4 * beat * SR)
for i, root in enumerate(bass_roots):
    for b in range(8):                      # eighth-note bass
        n = int(step * SR)
        p = i * bar + b * n
        note = root - 12 + (7 if b % 4 == 2 else 0)
        bass[p:p + n] += 0.22 * tri(hz(note), n) * env(n, release=0.05)
drum = np.zeros(total)
rng = np.random.default_rng(1)
for k in range(32):                         # kick on beats, hat on offbeats
    p = int(k * beat * SR)
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    drum[p:p + n] += 0.35 * np.sin(2 * np.pi * (110 - 600 * t) * t) * np.exp(-t * 28)
    p2 = p + int(step * SR)
    m = int(0.04 * SR)
    drum[p2:p2 + m] += 0.07 * rng.uniform(-1, 1, m) * np.linspace(1, 0, m)
write("menu_music.wav", lead + bass + drum)

# ---- UI sounds -------------------------------------------------------------------------
n = int(0.07 * SR)
write("ui_hover.wav", 0.25 * square(hz(12), n) * env(n, 0.002, 0.04))
n = int(0.18 * SR)
t = np.arange(n) / SR
click = 0.3 * square(hz(7), n // 2) * env(n // 2, 0.002, 0.03)
click2 = 0.3 * square(hz(14), n - n // 2) * env(n - n // 2, 0.002, 0.08)
write("ui_click.wav", np.concatenate([click, click2]))
n = int(0.12 * SR)
write("ui_back.wav", np.concatenate([0.3 * square(hz(7), n // 2) * env(n // 2, 0.002, 0.03),
                                     0.3 * square(hz(0), n // 2) * env(n // 2, 0.002, 0.06)]))
print("audio written to", OUT)
