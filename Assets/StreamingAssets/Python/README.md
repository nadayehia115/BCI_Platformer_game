# BCI Busters: the Python side

Unity starts these scripts silently (no console window) and stops them when it quits. You do not run them by hand, except to test.

| file | started by | what it does |
|---|---|---|
| `1-unicorn_steamer_hardware.py` | PLAY -> profile popup | streams the Unicorn on LSL (type `EEG`) and sends the electrode quality to Unity (UDP 5010) |
| `2-unicorn_streamer_offline_interaction_task_operator_view.py` | TrainingCalibration | UDP -> LSL marker bridge (a thread listens on 5007 and pushes every marker into the LSL stream `UnityMarkers` the moment it arrives) + records the EEG and the markers into a CSV |
| `3-unicorn_offline_feature_extraction.py` | loading screen | the 4 s flicker epochs (the one class) + the no-flicker reference windows -> SSVEP features (`bci_common.ssvep_features`) |
| `4-unicorn_offline_classification_model.py` | loading screen | trains the **one-class** focus model (`bci_model.FocusDetector`) on the focus epochs only; **accuracy >= 65 %** (focus recall + no-flicker rejection, cross-validated) -> `<ProfileID>.joblib`, otherwise `<ProfileID>_FAILED_DEBUG.joblib` + an empty `training_failed.flag`. **Demo user `test`: always saved as `<ProfileID>.joblib`** |
| `bci_game.py` | SessionCalibration / main level | live classifier: while the pad is on (UDP 5008 from Unity) it sends `switch` to Unity (UDP 5005) when it sees focus |
| `bci_model.py` | (imported by 4 and `bci_game.py`) | the one-class model + its cross-validation |
| `bci_common.py` | (imported) | profile, ports, signal preparation, features |

Everything reads `ActiveProfile.json` (Documents folder, written by Unity): ProfileID, pad frequency, folders. Data lives in `Documents/BCIBusters/data/<ProfileID>/offline`, models in `Documents/BCIBusters/models`.

## Python

Set `PythonInterpreterPath` in `Assets/Resources/BciSettings.asset` (Inspector) to the Python that has:

    pip install numpy scipy pandas scikit-learn joblib pylsl brainflow matplotlib

(`matplotlib` is optional: it only draws a grand-average picture.)

## Trying it without the headset

In `BciSettings` tick **Simulate Headset** (file 1 is started with `--simulate`; **Simulated Bad Electrodes**, e.g. `Pz=flat,Oz=noisy`, shows the red / yellow electrodes). The fake EEG has no SSVEP in it, so a training on it is expected to fail the 65 % test (and Unity sends you back to calibrate) - or pass by chance with very few trials. With the user name **test** the model is saved anyway.

## Demo shortcuts

* user name `test` (any case): its model is always saved (even with poor accuracy) and `test` can be created again and again (the old model is replaced);
* letter **O** on the headset-check screen goes on although not all electrodes are green.

## Where things are saved

* models: `Documents\BCIBusters\models\<ProfileID>.joblib` (e.g. `test_1920x1080_60Hz.joblib`; failed ones `..._FAILED_DEBUG.joblib` + `training_failed.flag`)
* recordings and features: `Documents\BCIBusters\data\<ProfileID>\offline\`
* the profile of the current run: `Documents\ActiveProfile.json`

## UDP map (localhost)

| port | listener | messages |
|---|---|---|
| 5005 | Unity (`BciListener`) | `ping` -> `pong`, `switch` |
| 5007 | file 2 | `marker\|1` the flicker starts (= the one class), `marker\|99` the flicker ended (rest begins), `marker\|98` discard (the player paused), `finish`, `quit` |
| 5008 | `bci_game.py` | `pad\|on`, `pad\|off`, `cal\|focus` (flicker starts), `cal\|end` (flicker ended), `cal\|discard`, `cal\|done`, `quit` |
| 5010 | Unity | `status\|Fz=GOOD,...`, `ready\|`, `error\|...`, `recorder_ready\|`, `classifier_ready\|` |
| 5011 | file 1 | `quit` |

## The calibration sequence and the one class

The game recognises ONE thing: the player looking at the crosshair in the middle of the flickering pad (it activates the helper's power).
The sequence follows the lab's Python paradigm:

| phase | seconds | screen | marker |
|---|---|---|---|
| instruction | 10 s after the scripts are ready | the crosshair and the instruction text, no flicker | - |
| PREPARE | 2, **once**, right before the first flicker | the pad's outline and the crosshair, still ("Get ready. Look at the crosshair in the middle.") | - |
| FLICKER | 4 | the pad flickers round the crosshair ("Focus on the crosshair ...") | `1` at its start, `99` at its end |
| REST | 2 | only the crosshair ("Rest. Relax your eyes and blink.") | - |

then FLICKER + REST again for every trial. Training = 20 trials (2 minutes), the warm-up of a returning user = 5 trials. In `BciSettings`,
**Prepare Every Trial** puts a Prepare before every flicker instead. The model is trained on the 4 s FOCUS epochs only. 4 s windows
without any flicker (the calm instruction time before the first flicker, or a long enough gap) are never a class: they only test how often
the model wrongly says "focus". A session calibration moves the live threshold halfway between today's focus scores and today's
no-flicker scores. A pause (`marker|98`) throws away what was running.
The calibration and training screens have no character (concentration!): the instruction is the text at the top centre. Tenna speaks only in the
setup screens (headset check, loading): his pictures are `Assets/deltarune/Tenna_*.png`, his voice `Assets/Resources/Audio/tenna_voice.mp3`.
The monitor-contrast screen (`BciContrast`) is switched off (`Show Contrast Screen` in `BciSettings`).
