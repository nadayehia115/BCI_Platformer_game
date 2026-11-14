"""Live SSVEP classifier of MainLevel (started silently by Unity; no window, no keyboard hooks).

It watches the player's EEG (file 1's LSL stream "EEG") and, while Unity shows the flickering pad on the black "superpower" screen,
decides whether the player is looking at the crosshair in the middle of it. The trained model (<ModelsDir>/<ProfileID>.joblib) has ONE
class, "focus on the crosshair of the flickering pad"; when it says yes for two checks in a row it sends the single UDP message that
brings the helper in:

    "switch"   ->  Unity's BciListener, 127.0.0.1:5005      (the same payload the old script sent for the T key)

The UDP link keeps the ping/pong handshake: the script pings Unity until it answers "pong", so it knows the level is up.

What Unity tells this script, on UDP 127.0.0.1:5008 (text messages):
    pad|on / pad|off          the flickering pad is shown / gone: decisions are made only while it is on
    cal|focus                 a SessionCalibration flicker starts (the quiet time before it - no flicker - is scored in 4 s windows as a reference)
    cal|end                   that flicker is over: its 4 s are scored as focus
    cal|discard               the player paused: forget what was running
    cal|done                  the calibration is over: the decision threshold is adapted to today's signal
    quit                      stop

    python bci_game.py --profile "C:\\Users\\me\\Documents\\ActiveProfile.json"
"""
import argparse
import os
import socket
import sys
import threading
import time

import joblib
import numpy as np

import bci_common as bc
from pylsl import StreamInlet, resolve_byprop

UNITY_ADDR = ("127.0.0.1", bc.PORT_GAME)          # the game (BciListener)
DECISION_PERIOD = 0.5                              # seconds between two looks at the signal while the pad is on
HITS_REQUIRED = 2                                  # focus must be seen this many times in a row
DEFAULT_THRESHOLD = 0.0                            # score of the one-class model (>= 0: it looks like the learned focus) above which a look counts
BUFFER_SECONDS = 13                                # room for 3 reference windows of 4 s
MAX_REFERENCE_WINDOWS = 3


class GameLink:
    """The UDP link to Unity: ping/pong handshake and the "switch" command."""

    def __init__(self):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self._last_pong = 0.0
        self._running = True
        threading.Thread(target=self._receive, daemon=True).start()
        threading.Thread(target=self._ping_loop, daemon=True).start()

    @property
    def is_up(self):
        """Unity answered a ping in the last 3 s."""
        return time.time() - self._last_pong < 3.0

    def switch(self):
        """The classifier found the player focusing on the pad: bring the helper in."""
        self._send("switch")

    def close(self):
        self._running = False
        try:
            self.sock.close()
        except OSError:
            pass

    def _send(self, text):
        self.sock.sendto(text.encode(), UNITY_ADDR)

    def _ping_loop(self):
        """Unity's listener only exists while the level is loaded: ping every second, so we know when it is up (and when it is gone)."""
        while self._running:
            try:
                self._send("ping")
            except OSError:
                return
            time.sleep(1.0)

    def _receive(self):
        while self._running:
            try:
                data, _ = self.sock.recvfrom(1024)
            except OSError:
                return
            if data.decode().startswith("pong"):
                if not self.is_up:
                    bc.log("Unity answered: the level is up.")
                self._last_pong = time.time()


class RingBuffer:
    """The last few seconds of EEG (samples x 8)."""

    def __init__(self, seconds=BUFFER_SECONDS, channels=len(bc.EEG_CHANNELS), fs=bc.FS):
        self.data = np.zeros((int(seconds * fs), channels))
        self.count = 0

    def push(self, chunk):
        chunk = np.asarray(chunk, dtype=float)
        n = len(chunk)
        if n == 0:
            return
        if n >= len(self.data):
            self.data[:] = chunk[-len(self.data):]
        else:
            self.data = np.roll(self.data, -n, axis=0)
            self.data[-n:] = chunk
        self.count += n

    def last(self, seconds, fs=bc.FS):
        n = int(seconds * fs)
        return self.data[-n:] if self.count >= n else None

    def window(self, k, seconds, fs=bc.FS):
        """The k-th window of `seconds` counted back from now (k = 1: the last one, k = 2: the one before it, ...), or None."""
        n = int(seconds * fs)
        if self.count < k * n or k * n > len(self.data):
            return None
        end = len(self.data) - (k - 1) * n
        return self.data[end - n: end]


def focus_score(bundle, window, line_freq):
    """How much this 4 s window looks like the learned focus class (one-class model: higher = more like it, >= threshold = focus)."""
    feats = bc.ssvep_features(window, bundle["frequency"], bc.FS, line_freq)
    return float(bundle["detector"].score(feats.reshape(1, -1))[0])


def main():
    bc.stdout_utf8()
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", default=None)
    parser.add_argument("--classifier-port", type=int, default=bc.PORT_CLASSIFIER)
    args = parser.parse_args()
    profile = bc.load_profile(args.profile)
    _, _, models_dir = bc.profile_dirs(profile)
    model_path = os.path.join(models_dir, f"{profile['ProfileID']}.joblib")
    if not os.path.exists(model_path):
        bc.log(f"ERROR: no trained model at {model_path}")
        return 1
    bundle = joblib.load(model_path)
    line_freq = float(bundle.get("line_frequency", bc.LINE_FREQ))
    bc.log(f"model {bundle.get('model_name')} loaded (accuracy {bundle.get('accuracy', 0) * 100:.1f}%, pad {bundle['frequency']} Hz)")
    default_threshold = float(bundle.get("threshold", DEFAULT_THRESHOLD))

    sock = bc.udp_listener(args.classifier_port)
    link = GameLink()

    inlet = None
    while inlet is None:
        streams = resolve_byprop("type", "EEG", timeout=3.0)
        if streams:
            inlet = StreamInlet(streams[0], max_buflen=30)
        else:
            bc.log("waiting for the EEG stream of file 1 ...")
            for text, _ in bc.udp_poll(sock):
                if text.lower() == "quit":
                    return 0
    bc.udp_send("classifier_ready|", bc.PORT_STATUS)              # Unity's session calibration waits for this
    bc.log("live classifier running.")

    buf = RingBuffer()
    threshold = default_threshold
    pad_on, pad_on_since, next_decision, hits = False, 0.0, 0.0, 0
    cal_running = False                              # a calibration flicker is running
    quiet_from = 0                                   # the sample count at which the no-flicker time began (start, or the end of the last flicker)
    cal_scores = {"focus": [], "reference": []}
    window_seconds = bc.EPOCH_SECONDS

    running = True
    while running:
        samples, _ = inlet.pull_chunk(timeout=0.05, max_samples=500)
        if samples:
            buf.push(samples)

        for text, _ in bc.udp_poll(sock):
            msg = text.lower()
            if msg == "quit":
                running = False
            elif msg == "pad|on":
                pad_on, pad_on_since, next_decision, hits = True, time.time(), 0.0, 0
                bc.log("pad on: listening for focus")
            elif msg == "pad|off":
                pad_on, hits = False, 0
                bc.log("pad off")
            elif msg == "cal|focus":
                cal_running = True
                # the quiet time before this flicker (the calm intro, or a long enough gap): 4 s windows counted back from now are the
                # no-flicker references the threshold is adapted with
                quiet_seconds = (buf.count - quiet_from) / bc.FS
                for k in range(1, MAX_REFERENCE_WINDOWS + 1):
                    if quiet_seconds + 0.1 < k * window_seconds:
                        break
                    window = buf.window(k, window_seconds)
                    if window is None:
                        break
                    try:
                        cal_scores["reference"].append(focus_score(bundle, window, line_freq))
                        bc.log(f"calibration no-flicker window {k}: score {cal_scores['reference'][-1]:.2f}")
                    except Exception as e:
                        bc.log(f"calibration reference skipped ({e})")
                quiet_from = buf.count                                # (a flicker is running now: nothing counts as quiet until it ends)
            elif msg == "cal|end":
                window = buf.last(window_seconds)
                if cal_running and window is not None:
                    try:
                        cal_scores["focus"].append(focus_score(bundle, window, line_freq))
                        bc.log(f"calibration focus: score {cal_scores['focus'][-1]:.2f}")
                    except Exception as e:
                        bc.log(f"calibration block skipped ({e})")
                cal_running = False
                quiet_from = buf.count
            elif msg == "cal|discard":
                if cal_running:                                       # a flicker was thrown away: the quiet time starts again now
                    quiet_from = buf.count
                cal_running = False
            elif msg == "cal|done":
                f_scores, r_scores = cal_scores["focus"], cal_scores["reference"]
                if len(f_scores) >= 2 and len(r_scores) >= 1 and np.median(f_scores) > np.median(r_scores):
                    # halfway between today's focus scores and today's no-flicker scores (the scale of the scores depends on the day's signal)
                    threshold = float(0.5 * (np.median(f_scores) + np.median(r_scores)))
                    bc.log(f"session calibration: threshold set to {threshold:.2f} "
                           f"(focus median {np.median(f_scores):.2f}, no-flicker median {np.median(r_scores):.2f})")
                else:
                    bc.log("session calibration: no clear difference today, the default threshold stays")
                cal_scores = {"focus": [], "reference": []}

        now = time.time()
        if pad_on and now >= next_decision and now - pad_on_since >= window_seconds:
            next_decision = now + DECISION_PERIOD
            window = buf.last(window_seconds)
            if window is not None:
                try:
                    p = focus_score(bundle, window, line_freq)
                except Exception as e:
                    bc.log(f"decision skipped ({e})")
                    continue
                hits = hits + 1 if p >= threshold else 0
                bc.log(f"focus score = {p:.2f} (threshold {threshold:.2f}) hits {hits}/{HITS_REQUIRED}")
                if hits >= HITS_REQUIRED:
                    if link.is_up:
                        link.switch()
                        bc.log("FOCUS DETECTED -> sent: switch")
                    else:
                        bc.log("focus detected, but Unity's level is not answering yet")
                    pad_on, hits = False, 0           # the pad is used up; Unity says pad|on again at the next obstacle

    link.close()
    sock.close()
    bc.log("live classifier stopped.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
