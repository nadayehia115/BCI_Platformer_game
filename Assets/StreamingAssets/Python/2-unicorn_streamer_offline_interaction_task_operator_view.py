"""
Unicorn offline recorder (silent background version of the old "interaction task" script)
-----------------------------------------------------------------------------------------
Started by Unity when TrainingCalibration begins. There is NO window, NO PsychoPy and NO stimulus logic here any more: Unity draws
the flickering pad itself and tells this script what is going on.

  * the EEG comes from file 1's LSL stream (stream type "EEG"): file 1 owns the headset, so this script does not open BrainFlow,
  * Unity sends the markers as UDP text on port 5007 (the script stamps each with the LSL clock the moment it arrives; the delay on
    localhost is well under a millisecond, far below what a 4 s SSVEP epoch needs). They are also re-broadcast on an LSL marker stream
    ("UnityMarkers") so any LSL recorder can sync with them:
        marker|1    a FOCUS block starts     marker|2   an IGNORE block starts
        marker|99   a block ended            marker|98  the running block is discarded (the player paused)
        finish      the calibration is over -> save the CSV and exit        quit   -> same
  * the pad frequency / frame count / screen refresh are read from ActiveProfile.json (written by Unity); nothing is calculated here,
  * the CSV has the columns file 3 expects: Timestamp, the 8 channels, Marker_Spike, Continuous_Marker, Artifact_Flag, plus the
    _frequencies.txt next to it.

    python 2-unicorn_streamer_offline_interaction_task_operator_view.py --profile "C:\\Users\\me\\Documents\\ActiveProfile.json"
"""
import argparse
import os
import sys
import time

import numpy as np
import pandas as pd
from pylsl import StreamInfo, StreamOutlet, StreamInlet, resolve_byprop, local_clock
from scipy import signal

import bci_common as bc

ARTIFACT_PTP_UV = 300.0          # peak-to-peak (after detrend) in a 0.5 s window above which the sample is flagged


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
        f.write(f"Ignore, {bc.MARK_IGNORE}, {frames}, {freq:.3f}\n")
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

    sock = bc.udp_listener(args.marker_port)                     # open first, so no early marker from Unity is lost
    inlet = find_eeg_stream()
    if inlet is None:
        bc.log("ERROR: no EEG stream found (is file 1 running?)")
        sock.close()
        return 1

    marker_outlet = StreamOutlet(StreamInfo("UnityMarkers", "Markers", 1, 0.0, "int32", "unity-markers"))
    bc.udp_send("recorder_ready|", bc.PORT_STATUS)               # Unity waits for this before it sends the first marker
    bc.log("recording: waiting for markers from Unity")

    times, chunks, markers = [], [], []
    running = True
    while running:
        samples, stamps = inlet.pull_chunk(timeout=0.05, max_samples=500)
        if samples:
            chunks.extend(samples)
            times.extend(stamps)
        for text, _ in bc.udp_poll(sock):
            now = local_clock()
            low = text.lower()
            if low.startswith("marker|"):
                try:
                    code = int(float(low.split("|", 1)[1]))
                except ValueError:
                    continue
                markers.append((now, code))
                marker_outlet.push_sample([code], now)
                bc.log(f"marker {code}")
            elif low in ("finish", "quit", "stop"):
                running = False

    # a last look at the samples that arrived while the last marker was being handled
    samples, stamps = inlet.pull_chunk(timeout=0.2, max_samples=2000)
    if samples:
        chunks.extend(samples)
        times.extend(stamps)
    save_recording(times, chunks, markers, profile, offline_dir)
    sock.close()
    bc.log("recorder finished.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
