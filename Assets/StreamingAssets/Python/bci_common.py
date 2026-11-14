"""Shared helpers of the BCI Busters Python side (files 1-4 and bci_game.py).

What lives here, so that training (file 3) and the live game (bci_game.py) compute EXACTLY the same features:
  * the profile that Unity writes (ActiveProfile.json) and the ports everybody talks on,
  * the signal preparation of one 4 s epoch (detrend, mains notch, 1-40 Hz band-pass),
  * the SSVEP features of one epoch for the pad frequency of the profile (filter-bank CCA + spectral SNR).

Nothing here opens a window or touches the headset.
"""
import json
import os
import socket
import sys
import time

import numpy as np
from scipy import signal

# ---- the headset ----
EEG_CHANNELS = ["Fz", "C3", "Cz", "C4", "Pz", "PO7", "Oz", "PO8"]
OCC_CHANNELS = ["Oz", "PO7", "PO8"]                     # the channels over the visual cortex: where the SSVEP shows
OCC_IDX = [EEG_CHANNELS.index(c) for c in OCC_CHANNELS]
FS = 250
LINE_FREQ = 50.0                                        # mains frequency (60.0 in the Americas)
EPOCH_SECONDS = 4.0                                     # one training / live window = the 4 s the pad flickers (as in the lab paradigm)
EPOCH_OFFSET_SECONDS = 0.0                              # nothing is skipped: the 2 s "prepare" before the flicker lets the eyes settle

# One trial of the calibration (the same sequence as the lab's SSVEP paradigm):  PREPARE 2 s (the pad's outline and the crosshair,
# no flicker)  ->  FLICKER 4 s (marker 1 at its start, the player looks at the crosshair)  ->  REST 2 s (marker 99 at its start)

# ---- markers Unity sends while the calibration runs ----
MARK_FOCUS = 1          # the pad flickers and the player looks at the crosshair in its middle - THE ONE CLASS of the game
MARK_END = 99           # the flicker is over, the rest begins
MARK_DISCARD = 98       # what was running is thrown away (the player paused)

# ---- UDP ports (localhost) ----
PORT_GAME = 5005        # Unity's BciListener: ping / switch
PORT_MARKERS = 5007     # file 2 listens: markers from Unity
PORT_CLASSIFIER = 5008  # bci_game.py listens: pad on/off + session calibration from Unity
PORT_STATUS = 5010      # Unity listens: electrode statuses from file 1
PORT_CONTROL = 5011     # file 1 listens: "quit"


def stdout_utf8():
    """Unity reads our output through a pipe: print without crashing on any character."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


# ------------------------------------------------------------------ profile
def default_profile_path():
    docs = os.path.join(os.path.expanduser("~"), "Documents")
    return os.path.join(docs, "ActiveProfile.json")


def load_profile(path=None):
    """Read ActiveProfile.json (written by Unity's BCIProfileManager) into a dict."""
    path = path or default_profile_path()
    with open(path, "r", encoding="utf-8-sig") as f:
        profile = json.load(f)
    profile["_path"] = path
    return profile


def profile_dirs(profile):
    """(data_dir, offline_dir, models_dir) of a profile; created if they are missing."""
    data_dir = profile["DataDir"]
    offline_dir = os.path.join(data_dir, "offline")
    models_dir = profile["ModelsDir"]
    for d in (data_dir, offline_dir, models_dir):
        os.makedirs(d, exist_ok=True)
    return data_dir, offline_dir, models_dir


# ------------------------------------------------------------------ signal preparation
def prepare_epoch(epoch, fs=FS, line_freq=LINE_FREQ):
    """One epoch (n_samples x n_channels, microvolts) -> detrended, mains-notched, 1-40 Hz band-passed copy."""
    x = np.asarray(epoch, dtype=np.float64)
    x = signal.detrend(x, axis=0, type="linear")
    b, a = signal.iirnotch(line_freq, 30.0, fs)
    x = signal.filtfilt(b, a, x, axis=0)
    b, a = signal.butter(4, [1.0, 40.0], btype="bandpass", fs=fs)
    return signal.filtfilt(b, a, x, axis=0)


# ------------------------------------------------------------------ SSVEP features
def cca_max_corr(x, y):
    """Largest canonical correlation between the columns of x and of y (numpy only, fast)."""
    x = x - x.mean(axis=0)
    y = y - y.mean(axis=0)
    qx, _ = np.linalg.qr(x)
    qy, _ = np.linalg.qr(y)
    s = np.linalg.svd(qx.T @ qy, compute_uv=False)
    return float(min(1.0, s[0])) if s.size else 0.0


def reference_signals(freq, n_samples, fs=FS, harmonics=3):
    t = np.arange(n_samples) / fs
    cols = []
    for h in range(1, harmonics + 1):
        cols.append(np.sin(2 * np.pi * h * freq * t))
        cols.append(np.cos(2 * np.pi * h * freq * t))
    return np.column_stack(cols)


def fbcca_score(epoch, freq, fs=FS, harmonics=3):
    """Filter-bank CCA (Chen et al. 2015): the combined correlation of the epoch with the harmonics of `freq`.
    Sub-band m keeps the m-th harmonic and above, so the weights w(m) = m^-1.25 + 0.25 favour the fundamental.
    The sub-bands start just under each harmonic (the usual 8 Hz steps suit 8-15 Hz targets, not the slow pad here)."""
    ref = reference_signals(freq, epoch.shape[0], fs, harmonics)
    nyq = fs / 2.0
    rho = 0.0
    for m in range(1, harmonics + 1):
        lo = max(1.0, freq * m - 1.5)
        hi = 40.0
        if lo >= hi - 1.0:
            break
        b, a = signal.butter(4, [lo / nyq, hi / nyq], btype="bandpass")
        sub = signal.filtfilt(b, a, epoch, axis=0)
        r = cca_max_corr(sub, ref)
        rho += (m ** -1.25 + 0.25) * r * r
    return rho


def band_snr_db(avg, freq, fs=FS):
    """Power at `freq` over the average power of its neighbouring bins (0.75-2 Hz away), in dB."""
    f, p = signal.welch(avg, fs=fs, nperseg=len(avg), noverlap=0, detrend="constant")
    i = int(np.argmin(np.abs(f - freq)))
    peak = p[max(0, i - 1): i + 2].max()
    df = f[1] - f[0]
    near = (np.abs(f - freq) >= 0.75) & (np.abs(f - freq) <= 2.0)
    noise = p[near].mean() if near.any() else p.mean()
    return float(10.0 * np.log10((peak + 1e-12) / (noise + 1e-12)))


FEATURE_NAMES = ["rho_f", "rho_ctrl_low", "rho_ctrl_high", "rho_diff", "snr_f_db", "snr_2f_db"]


def ssvep_features(epoch, freq, fs=FS, line_freq=LINE_FREQ, already_prepared=False):
    """The 6 features of one epoch (n_samples x 8 channels, raw microvolts) for the pad frequency `freq`:
       rho_f          filter-bank CCA with the pad frequency (occipital channels)
       rho_ctrl_low   the same with a frequency 20 % lower, rho_ctrl_high 25 % higher (nothing flickers there): the chance level
       rho_diff       rho_f minus the mean of the two controls
       snr_f_db       spectral peak at the pad frequency over its neighbourhood, in dB
       snr_2f_db      the same at the second harmonic"""
    x = np.asarray(epoch, dtype=np.float64)
    if not already_prepared:
        x = prepare_epoch(x, fs, line_freq)
    occ = x[:, OCC_IDX]
    rho = fbcca_score(occ, freq, fs)
    lo = fbcca_score(occ, freq * 0.8, fs)
    hi = fbcca_score(occ, freq * 1.25, fs)
    avg = occ.mean(axis=1)
    return np.array([rho, lo, hi, rho - 0.5 * (lo + hi), band_snr_db(avg, freq, fs), band_snr_db(avg, 2 * freq, fs)])


# ------------------------------------------------------------------ small UDP helper
def udp_send(text, port, host="127.0.0.1"):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.sendto(text.encode("utf-8"), (host, port))
    finally:
        s.close()


def udp_listener(port, host="127.0.0.1"):
    """A non-blocking UDP socket bound to `port` (SO_REUSEADDR: a leftover socket of a crashed run must not block us)."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind((host, port))
    s.setblocking(False)
    return s


def udp_poll(sock):
    """Everything waiting on the socket, as a list of (text, address)."""
    out = []
    while True:
        try:
            data, addr = sock.recvfrom(4096)
        except (BlockingIOError, InterruptedError):
            break
        except OSError:
            break
        out.append((data.decode("utf-8", errors="replace").strip(), addr))
    return out


def log(*parts):
    print(time.strftime("%H:%M:%S"), *parts, flush=True)
