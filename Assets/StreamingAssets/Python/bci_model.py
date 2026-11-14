"""The game's classifier: ONE class only.

The class is "the player looks at the crosshair in the middle of the flickering pad" (that is what activates the helper's power).
The model learns how strong the pad's flicker frequency shows in the EEG of such moments - from those moments alone - and later answers
one question for every 4 s window: "is the response at least about as strong as in the focus epochs I learned?" (score >= threshold: yes).

It lives in its own module (not in file 4) so that joblib can load the saved model again inside bci_game.py: the pickle points to
`bci_model.FocusDetector`, and both scripts find this file next to them.

How it works (a one-class, one-sided "typicality" model - robust with the ~12-20 epochs of a calibration, where a flexible one-class
SVM over-fits and throws half of the new focus epochs away):
  * only the EVIDENCE features are used: the filter-bank CCA score at the pad frequency (rho_f), its margin over the neighbouring
    frequencies (rho_diff) and the spectral SNR at the pad frequency (snr_f_db);
  * for each of them the model keeps the TYPICAL FOCUS LEVEL (the 25th percentile of the focus epochs) and the spread (std);
  * a window's distance = how far it falls BELOW that level, in spreads (RMS over the features). Stronger than typical costs nothing:
    a very clear SSVEP must never count as "unusual";
  * score = 1 - distance / radius, where the radius is the distance that `keep` (90 %) of the focus epochs stay within.
    score >= 0: looks like focus.
The windows WITHOUT flicker (2 s rest + 2 s prepare between two flickers) are NOT a second class: the model never sees them. They only
serve to measure how often the detector wrongly says "focus" when the pad is not flickering (see evaluate()).
"""
import numpy as np
from sklearn.model_selection import KFold

import bci_common as bc

EVIDENCE_FEATURES = ("rho_f", "rho_diff", "snr_f_db")     # "more" is never "less focus"
RANDOM_STATE = 42
DEFAULT_THRESHOLD = 0.0                                    # score >= 0: within what was learned


class FocusDetector:
    """One-class model of the focus class. fit() takes the features of focus epochs only (rows of bci_common.FEATURE_NAMES)."""

    def __init__(self, keep=0.9, level=0.25):
        self.keep = keep               # the share of the focus epochs that must score >= 0
        self.level = level             # the percentile of the focus epochs taken as the "typical" level
        self.cols = [bc.FEATURE_NAMES.index(n) for n in EVIDENCE_FEATURES]
        self.mu = None
        self.sd = None
        self.radius = 1.0

    def _distance(self, X):
        X = np.nan_to_num(np.atleast_2d(np.asarray(X, dtype=float)))[:, self.cols]
        shortfall = np.minimum((X - self.mu) / self.sd, 0.0)            # only "weaker than typical" counts
        return np.sqrt(np.mean(shortfall ** 2, axis=1))

    def fit(self, X):
        X = np.nan_to_num(np.asarray(X, dtype=float))[:, self.cols]
        self.mu = np.quantile(X, self.level, axis=0)
        self.sd = np.std(X, axis=0) + 1e-9
        self.radius = max(float(np.quantile(self._distance_selected(X), self.keep)), 1e-6)
        return self

    def _distance_selected(self, X_selected):
        shortfall = np.minimum((X_selected - self.mu) / self.sd, 0.0)
        return np.sqrt(np.mean(shortfall ** 2, axis=1))

    def score(self, X):
        """Higher = more like the focus epochs the model learned (1 = typical or stronger, 0 = at the edge, below 0 = not focus)."""
        return 1.0 - self._distance(X) / self.radius


def evaluate(X_focus, X_reference, keep, n_splits=5):
    """Cross-validation without leakage: every focus epoch is judged by a model that never saw it.
    recall      = share of the held-out focus epochs the detector accepts
    specificity = share of the no-flicker reference windows it correctly does NOT accept (None when there are none)
    accuracy    = their mean (just the recall when there are no reference windows)"""
    n = len(X_focus)
    kf = KFold(n_splits=int(max(2, min(n_splits, n))), shuffle=True, random_state=RANDOM_STATE)
    accepted = 0
    rejected_ref = []
    for train, test in kf.split(X_focus):
        det = FocusDetector(keep).fit(X_focus[train])
        accepted += int(np.sum(det.score(X_focus[test]) >= DEFAULT_THRESHOLD))
        if len(X_reference):
            rejected_ref.append(float(np.mean(det.score(X_reference) < DEFAULT_THRESHOLD)))
    recall = accepted / n
    specificity = float(np.mean(rejected_ref)) if rejected_ref else None
    accuracy = recall if specificity is None else 0.5 * (recall + specificity)
    return {"keep": keep, "recall": recall, "specificity": specificity, "accuracy": accuracy}
