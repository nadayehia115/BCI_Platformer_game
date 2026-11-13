"""
Unicorn BCI offline classifier (silent version, SSVEP "focus the pad" paradigm)
-------------------------------------------------------------------------------
Started by Unity's loading screen after file 3:

    python 4-unicorn_offline_classification_model.py --profile "C:\\Users\\me\\Documents\\ActiveProfile.json"

Trains two classifiers (Random Forest and an RBF SVM) on  <DataDir>/offline/ML_features_SSVEP.csv  (focus vs ignore),
measures their accuracy with stratified cross-validation (every epoch is predicted by a model that never saw it: no leakage),
keeps the better one and ALWAYS saves it - but under a name that tells Unity whether it is good enough:

    accuracy >= 65 %   ->  <ModelsDir>/<ProfileID>.joblib
    accuracy <  65 %   ->  <ModelsDir>/<ProfileID>_FAILED_DEBUG.joblib   and an empty file  <ModelsDir>/training_failed.flag

so the poor data stays available for debugging, while Unity (the loading screen) sees the flag and sends the player back to
the calibration. The model file is a joblib bundle: {"pipeline", "classes", "frequency", "feature_names", "accuracy", ...}.
"""
import argparse
import os
import sys

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.model_selection import StratifiedKFold, cross_val_predict
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import StandardScaler
from sklearn.svm import SVC
from sklearn.metrics import accuracy_score, balanced_accuracy_score, confusion_matrix, cohen_kappa_score

import bci_common as bc

ACCURACY_THRESHOLD = 0.65         # below this the model is saved as *_FAILED_DEBUG and the retry flag is written
RANDOM_STATE = 42
FLAG_NAME = "training_failed.flag"


def build_models():
    return [
        ("Random Forest", Pipeline([("clf", RandomForestClassifier(n_estimators=300, class_weight="balanced",
                                                                   random_state=RANDOM_STATE))])),
        ("RBF Kernel SVM", Pipeline([("scaler", StandardScaler()),
                                     ("clf", SVC(kernel="rbf", C=1.0, gamma="scale", probability=True,
                                                 class_weight="balanced", random_state=RANDOM_STATE))])),
    ]


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
    y = df["Target_Class"].to_numpy()
    X = df.drop(columns=["Target_Class"]).to_numpy()
    names = [c for c in df.columns if c != "Target_Class"]
    classes = np.unique(y)
    counts = {int(c): int((y == c).sum()) for c in classes}
    bc.log(f"{len(y)} epochs, classes {counts}")

    results = []
    can_validate = len(classes) == 2 and min(counts.values()) >= 2
    if can_validate:
        n_splits = int(max(2, min(5, min(counts.values()))))
        cv = StratifiedKFold(n_splits=n_splits, shuffle=True, random_state=RANDOM_STATE)
        for name, pipe in build_models():
            pred = cross_val_predict(pipe, X, y, cv=cv)                       # out-of-fold predictions
            acc = float(accuracy_score(y, pred))
            results.append({"name": name, "pipe": pipe, "accuracy": acc, "balanced": float(balanced_accuracy_score(y, pred)),
                            "kappa": float(cohen_kappa_score(y, pred)), "cm": confusion_matrix(y, pred)})
            bc.log(f"{name}: {n_splits}-fold accuracy {acc * 100:.1f}%  balanced {results[-1]['balanced'] * 100:.1f}%  "
                   f"kappa {results[-1]['kappa']:.2f}")
    else:
        bc.log("WARNING: not enough epochs of both classes to cross-validate; the model cannot be trusted")
        for name, pipe in build_models():
            results.append({"name": name, "pipe": pipe, "accuracy": 0.0, "balanced": 0.0, "kappa": 0.0, "cm": None})

    best = max(results, key=lambda r: r["accuracy"])
    accuracy = best["accuracy"]
    best["pipe"].fit(X, y)                                                    # the deployed model learns from ALL epochs

    bundle = {"pipeline": best["pipe"], "model_name": best["name"], "classes": classes, "profile_id": profile_id,
              "frequency": float(profile["TrueFrequency"]), "feature_names": names, "accuracy": accuracy,
              "epoch_seconds": bc.EPOCH_SECONDS, "line_frequency": float(profile.get("LineFrequency", bc.LINE_FREQ))}

    # ---- the explicit data-sufficiency check ----
    if accuracy >= ACCURACY_THRESHOLD:
        model_path = os.path.join(models_dir, f"{profile_id}.joblib")
        joblib.dump(bundle, model_path)
        if os.path.exists(flag_path):
            os.remove(flag_path)                                              # an old failure must not hide this success
        bc.log(f"MODEL SAVED ({best['name']}, accuracy {accuracy * 100:.1f}% >= {ACCURACY_THRESHOLD * 100:.0f}%): {model_path}")
    else:
        model_path = os.path.join(models_dir, f"{profile_id}_FAILED_DEBUG.joblib")
        joblib.dump(bundle, model_path)                                       # keep the bad data's model for debugging
        open(flag_path, "w").close()                                          # empty file: Unity sends the player back to calibrate
        bc.log(f"MODEL TOO WEAK ({best['name']}, accuracy {accuracy * 100:.1f}% < {ACCURACY_THRESHOLD * 100:.0f}%): "
               f"saved for debugging as {model_path}; wrote {flag_path}")

    with open(os.path.join(offline_dir, "report_SSVEP_COMPARISON.txt"), "w", encoding="utf-8") as f:
        f.write(f"CLASSIFIER COMPARISON ({profile_id}, SSVEP, pad {profile['TrueFrequency']} Hz)\n")
        f.write(f"Cross-validated (out-of-fold). Threshold for a usable model: {ACCURACY_THRESHOLD * 100:.0f}%\n\n")
        for r in results:
            f.write(f"{r['name']}: accuracy {r['accuracy'] * 100:.2f}%  balanced {r['balanced'] * 100:.2f}%  kappa {r['kappa']:.3f}\n")
            if r["cm"] is not None:
                f.write(f"confusion matrix (rows = true 1 focus / 2 ignore):\n{r['cm']}\n")
        f.write(f"\nSaved: {model_path}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
