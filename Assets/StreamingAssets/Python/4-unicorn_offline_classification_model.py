"""
Unicorn BCI offline classifier (silent version, ONE class: "looking at the crosshair of the flickering pad")
------------------------------------------------------------------------------------------------------------
Started by Unity's loading screen after file 3:

    python 4-unicorn_offline_classification_model.py --profile "C:\\Users\\me\\Documents\\ActiveProfile.json"

The game needs to recognise ONE thing: the player looking at the crosshair in the middle of the flickering pad (that activates the
helper's power). So the model has ONE class. It is a one-class model (bci_model.FocusDetector: how strong the pad frequency shows in
the EEG of focus epochs) trained ONLY on the focus epochs of <DataDir>/offline/ML_features_SSVEP.csv (Target_Class 1).

How good is it? Cross-validation without leakage: every focus epoch is judged by a model that never saw it (recall), and the windows
without flicker (Target_Class 0: the 2 s rest + 2 s prepare between two flickers) are used ONLY as a test: how many of them are
correctly NOT accepted (specificity). accuracy = mean(recall, specificity). The model is always saved, under a name that tells Unity
whether it is good enough:

    accuracy >= 65 %   ->  <ModelsDir>/<ProfileID>.joblib
    accuracy <  65 %   ->  <ModelsDir>/<ProfileID>_FAILED_DEBUG.joblib   and an empty file  <ModelsDir>/training_failed.flag

DEMO EXCEPTION: for the user name "test" (profile "DemoMode") the model is ALWAYS saved as <ProfileID>.joblib, whatever its accuracy,
and the flag is not written - so a demonstration can go on with data that is not good.

so the poor data stays available for debugging, while Unity (the loading screen) sees the flag and sends the player back to
the calibration. The model file is a joblib bundle: {"detector", "classes": [1], "frequency", "feature_names", "accuracy", "threshold", ...}.
"""
import argparse
import os
import sys

import joblib
import numpy as np
import pandas as pd

import bci_common as bc
import bci_model as bm

ACCURACY_THRESHOLD = 0.65         # below this the model is saved as *_FAILED_DEBUG and the retry flag is written
FLAG_NAME = "training_failed.flag"
KEEP_CHOICES = (0.9, 0.8, 1.0)    # the share of the focus epochs the model must accept (its boundary): the best one wins


def main():
    bc.stdout_utf8()
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", default=None)
    args = parser.parse_args()
    profile = bc.load_profile(args.profile)
    _, offline_dir, models_dir = bc.profile_dirs(profile)
    profile_id = profile["ProfileID"]
    flag_path = os.path.join(models_dir, FLAG_NAME)

    feat_path = os.path.join(offline_dir, "ML_features_SSVEP.csv")
    if not os.path.exists(feat_path):
        bc.log(f"ERROR: feature matrix missing: {feat_path}")
        return 1
    df = pd.read_csv(feat_path)
    names = [c for c in df.columns if c != "Target_Class"]
    X_focus = df.loc[df["Target_Class"] == 1, names].to_numpy(dtype=float)           # the ONE class: the only thing trained on
    X_ref = df.loc[df["Target_Class"] == 0, names].to_numpy(dtype=float)             # no flicker: only used to test
    bc.log(f"{len(X_focus)} focus epochs (the one class), {len(X_ref)} no-flicker reference windows (test only)")
    if len(X_focus) == 0:
        bc.log("ERROR: no focus epoch in the feature matrix")
        return 1

    results = []
    can_validate = len(X_focus) >= 4
    if can_validate:
        for keep in KEEP_CHOICES:
            r = bm.evaluate(X_focus, X_ref, keep)
            results.append(r)
            spec = "n/a" if r["specificity"] is None else f"{r['specificity'] * 100:.1f}%"
            bc.log(f"focus model keep={keep}: focus recall {r['recall'] * 100:.1f}%  no-flicker rejected {spec}  -> accuracy {r['accuracy'] * 100:.1f}%")
        if not len(X_ref):
            bc.log("WARNING: there are no no-flicker reference windows: the accuracy is only the share of focus epochs recognised")
    else:
        bc.log("WARNING: fewer than 4 focus epochs: the model cannot be cross-validated and cannot be trusted")
        results = [{"keep": KEEP_CHOICES[0], "recall": 0.0, "specificity": None, "accuracy": 0.0}]

    best = max(results, key=lambda r: r["accuracy"])
    accuracy = best["accuracy"]
    detector = bm.FocusDetector(best["keep"]).fit(X_focus)                      # the deployed model learns from ALL focus epochs

    bundle = {"detector": detector, "model_name": f"One-class focus model, keep={best['keep']}", "classes": [bc.MARK_FOCUS],
              "profile_id": profile_id, "frequency": float(profile["TrueFrequency"]), "feature_names": names,
              "accuracy": accuracy, "recall": best["recall"], "specificity": best["specificity"],
              "threshold": bm.DEFAULT_THRESHOLD, "epoch_seconds": bc.EPOCH_SECONDS,
              "line_frequency": float(profile.get("LineFrequency", bc.LINE_FREQ))}

    demo = bool(profile.get("DemoMode")) or str(profile.get("Username", "")).strip().lower() == "test"

    # ---- the explicit data-sufficiency check ----
    if demo and accuracy < ACCURACY_THRESHOLD:
        model_path = os.path.join(models_dir, f"{profile_id}.joblib")
        joblib.dump(bundle, model_path)
        if os.path.exists(flag_path):
            os.remove(flag_path)
        bc.log(f"DEMO USER: accuracy {accuracy * 100:.1f}% is below {ACCURACY_THRESHOLD * 100:.0f}%, but the model is saved anyway: {model_path}")
    elif accuracy >= ACCURACY_THRESHOLD:
        model_path = os.path.join(models_dir, f"{profile_id}.joblib")
        joblib.dump(bundle, model_path)
        if os.path.exists(flag_path):
            os.remove(flag_path)                                              # an old failure must not hide this success
        bc.log(f"MODEL SAVED ({bundle['model_name']}, accuracy {accuracy * 100:.1f}% >= {ACCURACY_THRESHOLD * 100:.0f}%): {model_path}")
    else:
        model_path = os.path.join(models_dir, f"{profile_id}_FAILED_DEBUG.joblib")
        joblib.dump(bundle, model_path)                                       # keep the bad data's model for debugging
        open(flag_path, "w").close()                                          # empty file: Unity sends the player back to calibrate
        bc.log(f"MODEL TOO WEAK ({bundle['model_name']}, accuracy {accuracy * 100:.1f}% < {ACCURACY_THRESHOLD * 100:.0f}%): "
               f"saved for debugging as {model_path}; wrote {flag_path}")

    with open(os.path.join(offline_dir, "report_SSVEP_COMPARISON.txt"), "w", encoding="utf-8") as f:
        f.write(f"ONE-CLASS MODEL ({profile_id}, SSVEP, pad {profile['TrueFrequency']} Hz)\n")
        f.write(f"Trained only on {len(X_focus)} focus epochs; tested with {len(X_ref)} no-flicker windows. "
                f"Threshold for a usable model: {ACCURACY_THRESHOLD * 100:.0f}%\n\n")
        for r in results:
            spec = "n/a" if r["specificity"] is None else f"{r['specificity'] * 100:.2f}%"
            f.write(f"keep={r['keep']}: focus recall {r['recall'] * 100:.2f}%  no-flicker rejected {spec}  accuracy {r['accuracy'] * 100:.2f}%\n")
        f.write(f"\nSaved: {model_path}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
