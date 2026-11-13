"""
Unicorn BCI offline feature extraction (silent version, SSVEP "focus the pad" paradigm)
---------------------------------------------------------------------------------------
Started by Unity's loading screen right after the calibration (no window, no GUI):

    python 3-unicorn_offline_feature_extraction.py --profile "C:\\Users\\me\\Documents\\ActiveProfile.json"

Reads every  <DataDir>/offline/SSVEP_training_*.csv  recorded by file 2 (older calibrations of the same profile are used too),
cuts one 4 s epoch from 1 s after every FOCUS (marker 1) and IGNORE (marker 2) block start (blocks are 5 s long), drops the epochs that hit an artifact or a block that
was discarded (marker 98 inside the epoch), and writes
    <DataDir>/offline/ML_features_SSVEP.csv   one row per epoch: the 6 SSVEP features + Target_Class (1 focus, 2 ignore)
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

    files = sorted(glob.glob(os.path.join(offline_dir, "SSVEP_training_*.csv")))
    if not files:
        bc.log(f"ERROR: no SSVEP_training_*.csv in {offline_dir}")
        return 1
    bc.log(f"{len(files)} recording(s) found for profile {profile['ProfileID']}")

    epoch_len = int(bc.FS * bc.EPOCH_SECONDS)
    offset = int(bc.FS * bc.EPOCH_OFFSET_SECONDS)
    feats, labels, rejected, discarded = [], [], 0, 0
    focus_spec, ignore_spec = [], []
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

        for t in np.where(np.isin(marker, [bc.MARK_FOCUS, bc.MARK_IGNORE]))[0]:
            s = t + offset                                       # the epoch starts a second into the block
            if s + epoch_len > len(df):
                continue
            if np.any((discard_at >= t) & (discard_at < s + epoch_len)):
                discarded += 1
                continue
            if float(np.mean(artifact[s: s + epoch_len] != 0)) > MAX_ARTIFACT_FRAC:
                rejected += 1
                continue
            epoch = bc.prepare_epoch(eeg[s: s + epoch_len], bc.FS, line_freq)
            feats.append(bc.ssvep_features(epoch, freq, bc.FS, line_freq, already_prepared=True))
            labels.append(int(marker[t]))
            (focus_spec if marker[t] == bc.MARK_FOCUS else ignore_spec).append(epoch[:, bc.OCC_IDX].mean(axis=1))

    if not feats:
        bc.log("ERROR: no valid epochs after artifact removal")
        return 1

    feat_df = pd.DataFrame(np.array(feats), columns=bc.FEATURE_NAMES)
    feat_df["Target_Class"] = labels
    out_csv = os.path.join(offline_dir, "ML_features_SSVEP.csv")
    feat_df.to_csv(out_csv, index=False)

    counts = feat_df["Target_Class"].value_counts().sort_index().to_dict()
    with open(os.path.join(offline_dir, "quality_report_SSVEP.txt"), "w", encoding="utf-8") as f:
        f.write(f"Quality audit: SSVEP | {profile['ProfileID']}\n{'=' * 40}\n")
        f.write(f"Files processed: {len(files)}\nPad frequency: {freq} Hz\n")
        f.write(f"Epochs kept: {len(feat_df)}\nEpochs rejected (artifact): {rejected}\nEpochs discarded (paused): {discarded}\n")
        f.write(f"Epochs per class (1 focus, 2 ignore): {counts}\n")
    bc.log(f"features saved: {out_csv}   epochs per class {counts}   rejected {rejected}   discarded {discarded}")

    try:                                                         # a picture for the researcher; optional
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        from scipy import signal
        fig, ax = plt.subplots(figsize=(8, 4))
        for spec, name, color in ((focus_spec, "focus the pad", "tab:red"), (ignore_spec, "look at the dot", "tab:gray")):
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
