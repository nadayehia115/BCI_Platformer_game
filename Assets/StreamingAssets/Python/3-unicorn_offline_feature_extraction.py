"""
Unicorn BCI offline feature extraction (silent version, SSVEP "look at the crosshair of the flickering pad": ONE class)
--------------------------------------------------------------------------------------------------------------------
Started by Unity's loading screen right after the calibration (no window, no GUI):

    python 3-unicorn_offline_feature_extraction.py --profile "C:/Users/me/Documents/ActiveProfile.json"

The calibration is the lab paradigm: the player reads the instruction (no flicker), PREPARE 2 s (pad outline + crosshair, once), then
FLICKER 4 s (marker 1 at its start; the player looks at the crosshair in the middle of the flickering pad) and REST 2 s (marker 99 at its
start), again and again. Reads every  <DataDir>/offline/SSVEP_training_*.csv  recorded by file 2 (older calibrations of the same profile
are used too) and cuts

    * one 4 s FOCUS epoch from every marker 1 (the whole flicker), the ONE class of the game (Target_Class 1);
    * REFERENCE windows: 4 s windows of EEG without any flicker (up to 3 per quiet period, counted back from the next flicker start): the
      calm time before the first flicker, and any gap between two flickers that is long enough. They are NOT a class: file 4 never trains
      on them, it only measures how often the detector wrongly says "focus" without a flicker (Target_Class 0).

Epochs that hit an artifact or a discarded block (marker 98 = the player paused) are dropped, and write
    <DataDir>/offline/ML_features_SSVEP.csv   one row per window: the 6 SSVEP features + Target_Class (1 focus, 0 reference)
    <DataDir>/offline/quality_report_SSVEP.txt
    <DataDir>/offline/grand_avg_SSVEP.png     (when matplotlib is installed)
The features are computed by bci_common.ssvep_features - the SAME function the live classifier (bci_game.py) uses.
(The old Motor Imagery / P300 branches belong to the lab paradigms and are not part of the game's silent pipeline.)
"""
import argparse
import glob
import os
import sys

import numpy as np
import pandas as pd

import bci_common as bc

MAX_ARTIFACT_FRAC = 0.30          # reject an epoch if more than 30 % of its samples are artifact-flagged


def read_frequency(profile, csv_file):
    """The pad frequency of one recording: its _frequencies.txt (written by file 2), else the profile."""
    txt = csv_file.replace(".csv", "_frequencies.txt")
    if os.path.exists(txt):
        with open(txt, "r", encoding="utf-8") as f:
            lines = f.readlines()
        for line in lines[2:]:
            parts = line.strip().split(",")
            if len(parts) >= 2:
                try:
                    return float(parts[-1])
                except ValueError:
                    pass
    return float(profile["TrueFrequency"])


def main():
    bc.stdout_utf8()
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", default=None)
    args = parser.parse_args()
    profile = bc.load_profile(args.profile)
    _, offline_dir, _ = bc.profile_dirs(profile)
    line_freq = float(profile.get("LineFrequency", bc.LINE_FREQ))
    freq = float(profile["TrueFrequency"])
    demo = bool(profile.get("DemoMode")) or str(profile.get("Username", "")).strip().lower() == "test"
    if demo:
        bc.log("DEMO USER: artifact rejection is off (the demo may run with bad electrodes)")

    files = sorted(glob.glob(os.path.join(offline_dir, "SSVEP_training_*.csv")))
    if not files:
        bc.log(f"ERROR: no SSVEP_training_*.csv in {offline_dir}")
        return 1
    bc.log(f"{len(files)} recording(s) found for profile {profile['ProfileID']}")

    epoch_len = int(bc.FS * bc.EPOCH_SECONDS)
    offset = int(bc.FS * bc.EPOCH_OFFSET_SECONDS)
    tolerance = int(bc.FS * 0.1)                                 # a frame or two of jitter between Unity's timing and the samples
    max_reference_per_period = 3
    feats, labels, rejected, discarded = [], [], 0, 0
    focus_spec, reference_spec = [], []

    def too_dirty(artifact, a0, a1):
        return (not demo) and float(np.mean(artifact[a0:a1] != 0)) > MAX_ARTIFACT_FRAC

    for csv_file in files:
        f_rec = read_frequency(profile, csv_file)
        if abs(f_rec - freq) > 0.01:
            bc.log(f"skipping {os.path.basename(csv_file)}: it was recorded with a {f_rec} Hz pad, this profile uses {freq} Hz")
            continue
        df = pd.read_csv(csv_file)
        marker = df["Marker_Spike"].to_numpy()
        eeg = df[bc.EEG_CHANNELS].to_numpy(dtype=float)
        artifact = df["Artifact_Flag"].to_numpy() if "Artifact_Flag" in df.columns else np.zeros(len(df))
        discard_at = np.where(marker == bc.MARK_DISCARD)[0]
        end_at = np.where(marker == bc.MARK_END)[0]

        # the quiet periods (no flicker): from the start of the recording / the end of a flicker to the start of the next flicker
        quiet_periods, quiet_start, open_flicker = [], 0, False
        for i in np.where(marker != 0)[0]:
            code = int(marker[i])
            if code == bc.MARK_FOCUS:
                quiet_periods.append((quiet_start, int(i)))
                open_flicker = True
            elif code in (bc.MARK_END, bc.MARK_DISCARD) and open_flicker:
                quiet_start, open_flicker = int(i), False

        # ---- the FOCUS epochs: the 4 s the pad flickers ----
        for t in np.where(marker == bc.MARK_FOCUS)[0]:
            s0 = t + offset
            if s0 + epoch_len > len(df):
                continue
            if np.any((discard_at >= t) & (discard_at < s0 + epoch_len)):
                discarded += 1
            elif too_dirty(artifact, s0, s0 + epoch_len):
                rejected += 1
            else:
                epoch = bc.prepare_epoch(eeg[s0: s0 + epoch_len], bc.FS, line_freq)
                feats.append(np.nan_to_num(np.asarray(bc.ssvep_features(epoch, freq, bc.FS, line_freq, already_prepared=True), dtype=float)))
                labels.append(1)
                focus_spec.append(epoch[:, bc.OCC_IDX].mean(axis=1))

        # ---- the REFERENCE windows: 4 s without flicker, counted back from the next flicker start ----
        for q_start, q_end in quiet_periods:
            for k in range(1, max_reference_per_period + 1):
                r1 = q_end - (k - 1) * epoch_len
                r0 = r1 - epoch_len
                if r0 < max(0, q_start - tolerance):
                    break
                if np.any((discard_at >= r0) & (discard_at < r1)) or too_dirty(artifact, r0, r1):
                    continue
                epoch = bc.prepare_epoch(eeg[r0: r1], bc.FS, line_freq)
                feats.append(np.nan_to_num(np.asarray(bc.ssvep_features(epoch, freq, bc.FS, line_freq, already_prepared=True), dtype=float)))
                labels.append(0)
                reference_spec.append(epoch[:, bc.OCC_IDX].mean(axis=1))

    if not feats:
        bc.log(f"ERROR: no valid epochs after artifact removal (rejected {rejected}, discarded {discarded})")
        return 1

    if 1 not in labels:
        bc.log(f"ERROR: no valid FOCUS epoch (rejected {rejected}, discarded {discarded})")
        return 1

    feat_df = pd.DataFrame(np.array(feats), columns=bc.FEATURE_NAMES)
    feat_df["Target_Class"] = labels                              # 1 = focus (the one class), 0 = no-flicker reference (never trained on)
    out_csv = os.path.join(offline_dir, "ML_features_SSVEP.csv")
    feat_df.to_csv(out_csv, index=False)

    counts = feat_df["Target_Class"].value_counts().sort_index().to_dict()
    with open(os.path.join(offline_dir, "quality_report_SSVEP.txt"), "w", encoding="utf-8") as f:
        f.write(f"Quality audit: SSVEP | {profile['ProfileID']}\n{'=' * 40}\n")
        f.write(f"Files processed: {len(files)}\nPad frequency: {freq} Hz\n")
        f.write(f"Epochs kept: {len(feat_df)}\nEpochs rejected (artifact): {rejected}\nEpochs discarded (paused): {discarded}\n")
        f.write(f"Epochs per class (1 focus, 2 rest): {counts}\n")
    bc.log(f"features saved: {out_csv}   windows {counts} (1 focus, 0 reference)   rejected {rejected}   discarded {discarded}")

    try:                                                         # a picture for the researcher; optional
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        from scipy import signal
        fig, ax = plt.subplots(figsize=(8, 4))
        for spec, name, color in ((focus_spec, "pad flickers (focus)", "tab:red"), (reference_spec, "no flicker (reference)", "tab:gray")):
            if spec:
                f, p = signal.welch(np.array(spec), fs=bc.FS, axis=1, nperseg=len(spec[0]))
                ax.plot(f, p.mean(axis=0), label=name, color=color)
        ax.axvline(freq, color="k", linestyle="--")
        ax.axvline(2 * freq, color="k", linestyle=":")
        ax.set_xlim(1, 25)
        ax.set_xlabel("Frequency (Hz)")
        ax.set_ylabel("Power spectral density (uV^2/Hz)")
        ax.set_title(f"SSVEP occipital spectrum (Oz, PO7, PO8) - pad {freq} Hz")
        ax.legend()
        fig.tight_layout()
        fig.savefig(os.path.join(offline_dir, "grand_avg_SSVEP.png"))
    except Exception as e:
        bc.log(f"(no plot: {e})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
