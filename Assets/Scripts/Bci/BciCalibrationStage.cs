using System.Collections;
using TMPro;
using UnityEngine;

// Stage 3: the calibration, on pure black, with the SAME flickering pad as the main game (the SsvepPad prefab, at the frequency and
// size of the profile), a countdown timer in the corner and a PAUSE button. One class for the two scenes:
//
//   TrainingCalibration  (new user)   several trials; file 2 records the EEG with the markers Unity sends it (UDP 5007 -> LSL);
//                                     afterwards the loading screen trains the model
//   SessionCalibration   (old user)   a short session; the live classifier (bci_game.py) is told about the flickers and adapts
//                                     its threshold to today's signal; then the main level starts
//
// NO character here: the player must be able to concentrate. The instructions are plain text in the middle of the top edge, between
// the timer (left) and the PAUSE button (right).
//
// The player looks ONLY at the crosshair in the middle of the screen, the whole time. The classifier has ONE class: the player looking
// at the crosshair in the middle of the flickering pad. The sequence (the lab's Python paradigm):
//     PREPARE  2 s  ONCE, right before the first flicker: the pad's outline and the crosshair, nothing flickers   "Get ready ..."
//                   (BciSettings.PrepareEveryTrial puts a Prepare before every flicker instead)
//   then, for every trial:
//     FLICKER  4 s  the pad flickers round the crosshair  <- marker 1 at its start, marker 99 at its end (= the one class)
//     REST     2 s  the pad is gone, only the crosshair stays
// Before all of this the player reads the instruction for IntroSeconds (no flicker): that EEG is the "no flicker" reference the model is
// tested with.
// Markers: 1 the flicker starts, 99 it ended, 98 the player paused (what was running is thrown away).
public class BciCalibrationStage : BciStage
{
    enum Phase { Prepare, Flicker, Rest }

    bool isTraining;
    bool paused;
    bool phaseCompleted;
    TMP_Text timerLabel, instruction;
    GameObject padObject, pauseOverlay;
    Canvas padCanvas;
    SsvepPad pad;

    protected override bool AllowEscape => true;

    protected override void BuildScreen(Transform root)
    {
        isTraining = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == BciFlow.TrainingScene;

        // the pad, in the middle of the screen (its own canvas, so nothing else interferes with its frames)
        padCanvas = BciUi.NewCanvas("Pad Canvas", 20, transform);
        var prefab = Resources.Load<GameObject>("SsvepPad");
        if (prefab != null)
        {
            padObject = Instantiate(prefab, padCanvas.transform);
            padObject.name = "SSVEP Pad";
            var rect = (RectTransform)padObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            float scale = Mathf.Max(0.01f, padCanvas.scaleFactor);
            float diameter = SsvepSettings.PadPixels / scale;             // the profile's diameter (cm) as canvas units
            rect.localScale = Vector3.one * (diameter / 200f);            // the prefab's pad is 200 units across
            pad = padObject.GetComponent<SsvepPad>();
            pad.padIndex = 0;
            pad.followSettings = true;
            pad.Refresh();
            padObject.SetActive(false);
        }
        else Debug.LogWarning("BCI: Assets/Resources/SsvepPad.prefab is missing");

        // the timer (top left), the pause button (top right), the instruction between them (top centre)
        timerLabel = BciUi.Text("Timer", root, "", 64f, new Vector2(-760f, 440f), new Vector2(300f, 80f), new Color(0.75f, 0.75f, 0.75f));
        BciUi.NewButton("PAUSE", root, new Vector2(790f, 450f), new Vector2(240f, 84f), 42f, () => SetPaused(true));
        instruction = BciUi.Text("Instruction", root, "", 44f, new Vector2(20f, 445f), new Vector2(1160f, 150f),
                                 new Color(0.82f, 0.82f, 0.82f), TextAlignmentOptions.Center, true);
        instruction.enableAutoSizing = true;
        instruction.fontSizeMin = 28f;
        instruction.fontSizeMax = 44f;

        BuildPauseOverlay(root);
        StartCoroutine(Run());
    }

    void BuildPauseOverlay(Transform root)
    {
        var holder = BciUi.Rect("Pause Overlay", root);
        BciUi.Stretch(holder);
        pauseOverlay = holder.gameObject;
        var dim = BciUi.Panel("Dim", holder, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.92f), Vector2.zero);
        dim.raycastTarget = true;
        BciUi.Text("Paused", holder, "PAUSED", 130f, new Vector2(0f, 200f), new Vector2(900f, 160f), Color.white);
        BciUi.NewButton("RESUME", holder, new Vector2(0f, 20f), new Vector2(520f, 120f), 60f, () => SetPaused(false));
        BciUi.NewButton("EXIT", holder, new Vector2(0f, -140f), new Vector2(520f, 120f), 60f, BciFlow.Abort);
        pauseOverlay.SetActive(false);
    }

    void SetPaused(bool value)
    {
        paused = value;
        pauseOverlay.SetActive(value);
        if (padCanvas != null) padCanvas.enabled = !value;             // the pad's canvas is above the overlay: no crosshair on the PAUSED screen
    }

    // ---- sending what is happening ----

    // Training: the markers go to file 2 (the recorder), which stamps them with the LSL clock (BciMarkerSender, UDP 5007).
    // Session: the classifier hears about the flickers.
    void SendMarker(int code)
    {
        if (isTraining)
            BciMarkerSender.Send(code);
        else if (code == 1) BciUdp.Send("cal|focus", Settings.ClassifierPort);
        else if (code == 99) BciUdp.Send("cal|end", Settings.ClassifierPort);
        else if (code == 98) BciUdp.Send("cal|discard", Settings.ClassifierPort);
    }

    // ---- the run ----

    IEnumerator Run()
    {
        var manager = BciProcessManager.Instance;
        if (isTraining)
            manager.Launch("2-unicorn_streamer_offline_interaction_task_operator_view.py",
                           $"--profile {BciFlow.Quote(BciProfile.ActiveProfilePath)} --marker-port {Settings.MarkerPort}",
                           BciFlow.RecorderTag, Settings.MarkerPort);
        else
            BciFlow.StartClassifier();

        float prepareSeconds = Settings.PrepareSeconds, focusSeconds = Settings.FocusSeconds, restSeconds = Settings.RestSeconds;
        bool prepareEveryTrial = Settings.PrepareEveryTrial;
        float trialSeconds = (prepareEveryTrial ? prepareSeconds : 0f) + focusSeconds + restSeconds;
        // the timer counts down TrainingSeconds (2:00): the trials follow from it; the one Prepare of the default sequence is not in it,
        // the timer starts running with the first flicker
        int trials = Mathf.Max(1, Mathf.RoundToInt((isTraining ? Settings.TrainingSeconds : Settings.SessionSeconds) / trialSeconds));
        float total = trials * trialSeconds;
        UpdateTimer(total);

        // the instructions, while the script behind the scenes gets ready; the crosshair already shows where to look
        Show(Phase.Rest);
        instruction.text = isTraining
            ? "Keep your eyes on the crosshair in the middle of the screen the whole time. When the pad flickers around it, focus on the crosshair!"
            : "A quick warm-up! Keep looking at the crosshair. Focus on it while the pad flickers around it.";
        // (training: the recorder must have the EEG stream; session: the classifier must have loaded the model)
        float waitReady = 0f;
        while ((isTraining ? !manager.RecorderReady : !manager.ClassifierReady) && waitReady < 45f)
        {
            waitReady += Time.unscaledDeltaTime;
            yield return null;
        }
        float intro = 0f;                                              // calm time with a known no-flicker EEG
        while (intro < Settings.IntroSeconds)
        {
            intro += Time.unscaledDeltaTime;
            yield return null;
        }

        float done = 0f;
        if (!prepareEveryTrial)                                        // the one Prepare, right before the flickering starts
        {
            do { yield return RunPhase(Phase.Prepare, prepareSeconds, total, done); } while (!phaseCompleted);
        }
        for (int trial = 0; trial < trials; trial++)
        {
            // (PREPARE +) FLICKER; a flicker interrupted by a pause is thrown away and gets a new Prepare before it starts again
            bool needPrepare = prepareEveryTrial;
            while (true)
            {
                if (needPrepare)
                    do { yield return RunPhase(Phase.Prepare, prepareSeconds, total, done); } while (!phaseCompleted);
                yield return RunPhase(Phase.Flicker, focusSeconds, total, done + (prepareEveryTrial ? prepareSeconds : 0f));
                if (phaseCompleted) break;
                needPrepare = true;
            }
            float flickerStart = done + (prepareEveryTrial ? prepareSeconds : 0f);
            do { yield return RunPhase(Phase.Rest, restSeconds, total, flickerStart + focusSeconds); } while (!phaseCompleted);
            done += trialSeconds;
        }

        // the end
        HidePad();
        UpdateTimer(0f);
        instruction.text = "Great job! That was all I needed.";
        if (isTraining) BciMarkerSender.SendCommand("finish");
        else BciUdp.Send("cal|done", Settings.ClassifierPort);
        float wait = 0f;
        while (wait < 2f) { wait += Time.unscaledDeltaTime; yield return null; }

        if (isTraining) BciFlow.AfterTraining();
        else BciFlow.AfterSession();
    }

    static string TextFor(Phase phase)
    {
        switch (phase)
        {
            case Phase.Prepare: return "Get ready. Look at the crosshair in the middle.";
            case Phase.Flicker: return "Focus on the crosshair in the middle of the flickering pad!";
            default: return "Rest. Relax your eyes and blink.";
        }
    }

    // One phase of a trial. A pause throws it away (marker 98): phaseCompleted stays false and the caller runs it again from its beginning.
    IEnumerator RunPhase(Phase phase, float seconds, float total, float doneBefore)
    {
        while (paused) yield return null;
        Show(phase);
        instruction.text = TextFor(phase);
        if (phase == Phase.Flicker) SendMarker(1);

        phaseCompleted = true;
        bool ticks = phase != Phase.Prepare || Settings.PrepareEveryTrial;      // the lone Prepare does not move the timer
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (paused) { phaseCompleted = false; break; }
            elapsed += Time.unscaledDeltaTime;
            if (ticks) UpdateTimer(total - doneBefore - elapsed);
            yield return null;
        }
        if (!phaseCompleted)
        {
            SendMarker(98);
            HidePad();
            yield break;
        }
        if (phase == Phase.Flicker) SendMarker(99);
    }

    // What the pad looks like in a phase.
    void Show(Phase phase)
    {
        if (padObject == null || pad == null) return;
        padObject.SetActive(true);
        switch (phase)
        {
            case Phase.Prepare: pad.ShowOutline(); break;          // the pad's outline + the crosshair, still
            case Phase.Flicker: pad.ShowFlicker(); break;          // the pad flickers round the crosshair
            default: pad.ShowOnlyCrosshair(); break;               // only the crosshair, still
        }
    }

    void HidePad()
    {
        if (padObject != null) padObject.SetActive(false);
    }

    void UpdateTimer(float remaining)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, remaining));
        timerLabel.text = $"{s / 60}:{s % 60:00}";
    }
}
