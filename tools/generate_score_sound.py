"""Synthesises the short jingle that plays when a gem (score point) is collected.
Run from the project root:  python tools/generate_score_sound.py
It writes Assets/Resources/Audio/score_collect.wav (ScorePoint loads it from Resources when the gem has no sound of its own).
Replace the file with a real recording any time; keep the name."""
import os
import wave
import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets", "Resources", "Audio")
os.makedirs(OUT, exist_ok=True)


def tone(freq, seconds, decay):
    """A bright coin-like note: a soft square wave plus its octave, a quick attack and a smooth decay."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    wave_ = np.where((t * freq) % 1.0 < 0.3, 1.0, -1.0) * 0.55 + np.sin(2 * np.pi * freq * 2 * t) * 0.25
    env = np.exp(-t * decay)
    attack = min(n, int(0.004 * SR))
    env[:attack] *= np.linspace(0, 1, attack)
    release = min(n, int(0.01 * SR))
    env[-release:] *= np.linspace(1, 0, release)
    return wave_ * env


# C6 E6 G6 C7: a quick rising major arpeggio, the last note rings out
C6, E6, G6, C7 = 1046.5, 1318.5, 1568.0, 2093.0
notes = [(C6, 0.07, 14), (E6, 0.07, 14), (G6, 0.07, 14), (C7, 0.42, 7)]
parts = [tone(f, s, d) for f, s, d in notes]
sound = np.concatenate(parts)
sound = sound / np.max(np.abs(sound)) * 0.6                      # not too loud next to the other sounds

path = os.path.join(OUT, "score_collect.wav")
with wave.open(path, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((np.clip(sound, -1, 1) * 32767).astype("<i2").tobytes())
print("wrote", path, "%.2f s" % (len(sound) / SR))
