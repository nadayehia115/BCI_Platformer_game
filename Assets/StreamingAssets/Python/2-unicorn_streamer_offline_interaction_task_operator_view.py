"""
Unicorn offline recorder (silent background version of the old "interaction task" script)
-----------------------------------------------------------------------------------------
Started by Unity when TrainingCalibration begins. There is NO window, NO PsychoPy and NO stimulus logic here any more: Unity draws
the flickering pad itself and tells this script what is going on.

UDP-to-LSL marker workaround (Unity has no native LSL plugin):
  * Unity sends every event marker as a tiny UDP text payload to port 5007 of this computer (BciMarkerSender):
        marker|1    the FLICKER starts (the player looks at the crosshair in the middle of the pad) - the ONE class
        marker|99   the flicker ended, the rest begins      marker|98  what was running is discarded (the player paused)
        finish      the calibration is over -> save the CSV and exit      quit   -> same
  * a BACKGROUND THREAD of this script blocks on that socket; the moment a payload arrives it stamps it with the LSL clock
    (pylsl.local_clock, the clock of the EEG stream) and pushes it straight into an LSL StreamOutlet ("UnityMarkers", type "Markers"),
    so the precise LSL timestamping happens in Python, next to the EEG stream. Any LSL recorder can use that marker stream.
  * the EEG comes from file 1's LSL stream (type "EEG"): file 1 owns the headset, so this script does not open BrainFlow.
  * at the end the CSV has the columns file 3 expects: Timestamp, the 8 channels, Marker_Spike, Continuous_Marker, Artifact_Flag,
    and the _frequencies.txt next to it. The pad frequency / frame count / refresh come from ActiveProfile.json (written by Unity).

    python 2-unicorn_streamer_offline_interaction_task_operator_view.py --profile "C:\\Users\\me\\Documents\\ActiveProfile.json"
"""
import argparse
import os
import socket
import sys
import threading
import time

import numpy as np
import pandas as pd
from pylsl import StreamInfo, StreamOutlet, StreamInlet, resolve_byprop, local_clock
from scipy import signal

import bci_common as bc

ARTIFACT_PTP_UV = 300.0          # peak-to-peak (after detrend) in a 0.5 s window above which the sample is flagged


class MarkerBridge:
    """UDP port -> LSL marker outlet, on its own thread. Everything it receives is also kept (LSL time, code) for the CSV."""

    def __init__(self, port):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("127.0.0.1", port))                       # opened first, so no early marker from Unity is lost
        self.sock.settimeout(0.25)                                # wakes up now and then to notice the stop request
        self.outlet = StreamOutlet(StreamInfo("UnityMarkers", "Markers", 1, 0.0, "int32", "unity-markers"))
        self.markers = []                                         # (lsl time, code)
        self.lock = threading.Lock()
        self.finished = threading.Event()                         # "finish" / "quit" arrived
        self._stop = False
        self.thread = threading.Thread(target=self._listen, daemon=True, name="marker-listener")
        self.thread.start()

    def _listen(self):
        while not self._stop:
            try:
                data, _ = self.sock.recvfrom(4096)                # blocks until a payload arrives
            except socket.timeout:
                continue
            except OSError:
                return
            stamp = local_clock()                                 # stamped at the very moment it is received ...
            text = data.decode("utf-8", errors="replace").strip().lower()
            if text.startswith("marker|"):
                try:
                    code = int(float(text.split("|", 1)[1]))
                except ValueError:
                    continue
                self.outlet.push_sample([code], stamp)            # ... and pushed into LSL immediately
                with self.lock:
                    self.markers.append((stamp, code))
                bc.log(f"marker {code}")
            elif text in ("finish", "quit", "stop"):
                self.finished.set()

    def snapshot(self):
        with self.lock:
            return list(self.markers)

    def close(self):
        self._stop = True
        self.thread.join(timeout=1.0)
        self.sock.close()


def find_eeg_stream(timeout_total=40.0):
    """File 1 may still be starting: keep looking for its EEG stream."""
    end = time.time() + timeout_total
    while time.time() < end:
        streams = resolve_byprop("type", "EEG", timeout=2.0)
        if streams:
            return StreamInlet(streams[0], max_buflen=60)
        bc.log("waiting for the EEG stream of file 1 ...")
    return None


def save_recording(times, data, markers, profile, offline_dir):
    """Marker positions -> the sample they belong to; then the CSV and the frequencies file."""
    if len(times) == 0:
        bc.log("no EEG samples were recorded: nothing to save")
        return None
    times = np.asarray(times, dtype=np.float64)
    eeg = np.asarray(data, dtype=np.float64)                     # (n_samples, 8)

    spikes = np.zeros(len(times))
    for t_marker, code in markers:
        i = int(np.searchsorted(times, t_marker))               # the first sample at or after the marker
        if i >= len(times):
            i = len(times) - 1
        if spikes[i] == 0.0:
            spikes[i] = code
        else:                                                    # two markers on one sample: move the later one on
            j = min(i + 1, len(times) - 1)
            spikes[j] = code

    df = pd.DataFrame(eeg[:, :len(bc.EEG_CHANNELS)], columns=bc.EEG_CHANNELS)
    df.insert(0, "Timestamp", times)
    df["Marker_Spike"] = spikes

    blocks = df["Marker_Spike"].replace(0.0, np.nan).ffill().fillna(0.0)
    blocks[blocks == bc.MARK_END] = 0.0
    df["Continuous_Marker"] = blocks

    detr = df[bc.EEG_CHANNELS].apply(lambda x: signal.detrend(x))
    w = int(bc.FS * 0.5)
    ptp = detr.rolling(window=w, min_periods=1).max() - detr.rolling(window=w, min_periods=1).min()
    df["Artifact_Flag"] = (ptp > ARTIFACT_PTP_UV).any(axis=1).astype(int)

    stamp = time.strftime("%Y%m%d_%H%M%S")
    out_file = os.path.join(offline_dir, f"SSVEP_training_{stamp}.csv")
    df.to_csv(out_file, index=False)
    bc.log("data saved to:", out_file)

    refresh = float(profile.get("RefreshRate", 60.0))
    frames = int(profile.get("FrameCount", 0))
    freq = float(profile.get("TrueFrequency", 0.0))
    with open(out_file.replace(".csv", "_frequencies.txt"), "w", encoding="utf-8") as f:
        f.write(f"Screen Refresh Rate: {refresh:.2f} Hz\n")
        f.write("Class,Marker,Frames,Frequency_Hz\n")
        f.write(f"Focus, {bc.MARK_FOCUS}, {frames}, {freq:.3f}\n")
    return out_file


def main():
    bc.stdout_utf8()
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", default=None, help="path of ActiveProfile.json")
    parser.add_argument("--marker-port", type=int, default=bc.PORT_MARKERS)
    args = parser.parse_args()

    profile = bc.load_profile(args.profile)
    _, offline_dir, _ = bc.profile_dirs(profile)
    bc.log(f"profile {profile.get('ProfileID')}: pad {profile.get('TrueFrequency')} Hz "
           f"({profile.get('FrameCount')} frames at {profile.get('RefreshRate')} Hz)")

    bridge = MarkerBridge(args.marker_port)                      # the UDP -> LSL bridge starts listening at once
    inlet = find_eeg_stream()
    if inlet is None:
        bc.log("ERROR: no EEG stream found (is file 1 running?)")
        bridge.close()
        return 1

    bc.udp_send("recorder_ready|", bc.PORT_STATUS)               # Unity waits for this before it sends the first marker
    bc.log("recording: the marker bridge (UDP %d -> LSL 'UnityMarkers') is running" % args.marker_port)

    times, chunks = [], []
    while not bridge.finished.is_set():
        samples, stamps = inlet.pull_chunk(timeout=0.05, max_samples=500)
        if samples:
            chunks.extend(samples)
            times.extend(stamps)

    # a last look at the samples that arrived while the last marker was being handled
    samples, stamps = inlet.pull_chunk(timeout=0.2, max_samples=2000)
    if samples:
        chunks.extend(samples)
        times.extend(stamps)
    save_recording(times, chunks, bridge.snapshot(), profile, offline_dir)
    bridge.close()
    bc.log("recorder finished.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
