# BCI Busters: the Python side

Unity starts these scripts silently (no console window) and stops them when it quits. You do not run them by hand, except to test.

| file | started by | what it does |
|---|---|---|
| `1-unicorn_steamer_hardware.py` | PLAY -> profile popup | streams the Unicorn on LSL (type `EEG`) and sends the electrode quality to Unity (UDP 5010) |
| `2-unicorn_streamer_offline_interaction_task_operator_view.py` | TrainingCalibration | records the EEG + the markers Unity sends (UDP 5007) into a CSV |
| `3-unicorn_offline_feature_extraction.py` | loading screen | epochs -> SSVEP features (`bci_common.ssvep_features`) |
| `4-unicorn_offline_classification_model.py` | loading screen | trains Random Forest + SVM; **accuracy >= 65 %** -> `<ProfileID>.joblib`, otherwise `<ProfileID>_FAILED_DEBUG.joblib` + an empty `training_failed.flag` |
| `bci_game.py` | SessionCalibration / main level | live classifier: while the pad is on (UDP 5008 from Unity) it sends `switch` to Unity (UDP 5005) when it sees focus |
| `bci_common.py` | (imported) | profile, ports, signal preparation, features |

Everything reads `ActiveProfile.json` (Documents folder, written by Unity): ProfileID, pad frequency, folders. Data lives in `Documents/BCIBusters/data/<ProfileID>/offline`, models in `Documents/BCIBusters/models`.

## Python

Set `PythonInterpreterPath` in `Assets/Resources/BciSettings.asset` (Inspector) to the Python that has:

    pip install numpy scipy pandas scikit-learn joblib pylsl brainflow matplotlib

(`matplotlib` is optional: it only draws a grand-average picture.)

## Trying it without the headset

In `BciSettings` tick **Simulate Headset** (file 1 is started with `--simulate`; **Simulated Bad Electrodes**, e.g. `Pz=flat,Oz=noisy`, shows the red / yellow electrodes). The fake EEG has no SSVEP in it, so a training on it is expected to fail the 65 % test (and Unity sends you back to calibrate) - or pass by chance with very few trials.

## UDP map (localhost)

| port | listener | messages |
|---|---|---|
| 5005 | Unity (`BciListener`) | `ping` -> `pong`, `switch` |
| 5007 | file 2 | `marker\|1` focus, `marker\|2` ignore, `marker\|99` block end, `marker\|98` discard, `finish`, `quit` |
| 5008 | `bci_game.py` | `pad\|on`, `pad\|off`, `cal\|focus`, `cal\|ignore`, `cal\|end`, `cal\|done`, `quit` |
| 5010 | Unity | `status\|Fz=GOOD,...`, `ready\|`, `error\|...`, `recorder_ready\|`, `classifier_ready\|` |
| 5011 | file 1 | `quit` |
