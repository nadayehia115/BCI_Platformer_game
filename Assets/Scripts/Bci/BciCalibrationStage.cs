using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Stage 3: the calibration, on pure black, with the SAME flickering pad as the main game (the SsvepPad prefab, at the frequency and
// size of the profile), a countdown timer in the corner and a PAUSE button. One class for the two scenes:
//
//   TrainingCalibration  (new user)   several trials; file 2 records the EEG with the markers Unity sends it (UDP);
//                                     afterwards the loading screen trains the model
//   SessionCalibration   (old user)   a short 30 s session; the live classifier (bci_game.py) is told about the blocks and adapts
//                                     its threshold to today's signal; then the main level starts
//
// A trial = a FOCUS block (look at the flickering pad) + an IGNORE block (the pad flickers on, but the player looks at the dot in
// the corner). Markers: 1 focus starts, 2 ignore starts, 99 a block ended, 98 the running block is discarded (the player paused).
public class BciCalibrationStage : BciStage
{
    const int Dot = 0;

    bool isTraining;
    bool paused;
    TMP_Text timerLabel;
    GameObject padObject, dotObject, pauseOverlay;
    BciSpeechBubble bubble;
    RectTransform dotRect;

    protected override bool AllowEscape => true;

    protected override void BuildScreen(Transform root)
    {
        isTraining = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == BciFlow.TrainingScene;

        // the pad, in the middle of the screen (its own canvas, so nothing else interferes with its frames)
        var padCanvas = BciUi.NewCanvas("Pad Canvas", 20, transform);
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
            var pad = padObject.GetComponent<SsvepPad>();
            pad.padIndex = 0;
            pad.followSettings = true;
            pad.Refresh();
            padObject.SetActive(false);
        }
        else Debug.LogWarning("BCI: Assets/Resources/SsvepPad.prefab is missing");

        // the dot of the IGNORE blocks: a small white dot in the top left corner of the screen
        dotRect = BciUi.Rect("Look Here Dot", root);
        dotRect.sizeDelta = new Vector2(44f, 44f);
        BciUi.Anchor(dotRect, new Vector2(0.5f, 0.5f), new Vector2(-760f, 360f));
        var dotImage = dotRect.gameObject.AddComponent<Image>();
        dotImage.sprite = FountainFx.Dot;
        dotImage.raycastTarget = false;
        dotObject = dotRect.gameObject;
        dotObject.SetActive(false);

        // the timer (top left, below the dot), the pause button (top right)
        timerLabel = BciUi.Text("Timer", root, "", 64f, new Vector2(-760f, 440f), new Vector2(300f, 80f), new Color(0.75f, 0.75f, 0.75f));
        BciUi.NewButton("PAUSE", root, new Vector2(790f, 450f), new Vector2(240f, 84f), 42f, () => SetPaused(true));

        bubble = BciSpeechBubble.Create(root, new Vector2(0f, 0f), new Vector2(40f, 30f), 900f, 0.45f);

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
    }

    // ---- sending what is happening ----

    // Training: the markers go to file 2 (the recorder). Session: the classifier hears about the blocks.
    void SendMarker(int code)
    {
        if (isTraining)
            BciUdp.Send("marker|" + code, Settings.MarkerPort);
        else if (code == 1) BciUdp.Send("cal|focus", Settings.ClassifierPort);
        else if (code == 2) BciUdp.Send("cal|ignore", Settings.ClassifierPort);
        else if (code == 99 || code == 98) BciUdp.Send("cal|end", Settings.ClassifierPort);
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

        int trials = isTraining ? Settings.TrainingTrials : Settings.SessionTrials;
        float focusSeconds = Settings.FocusSeconds, ignoreSeconds = Settings.IgnoreSeconds;
        float total = trials * (focusSeconds + ignoreSeconds);
        UpdateTimer(total);

        // the character explains while the script behind the scenes gets ready
        bubble.Say(isTraining
            ? "Let's train your mind control! A pad will flicker. When the dot is hidden, stare at the pad. When the dot shows up in the " +
              "corner, look at the dot and ignore the pad."
            : "A quick warm-up! Stare at the pad while the dot is hidden, and look at the dot in the corner when it shows up.");
        float intro = 0f;
        // (training: the recorder must have the EEG stream; session: the classifier must have loaded the model)
        while (intro < 7f || (isTraining ? !manager.RecorderReady : !manager.ClassifierReady) && intro < 45f)
        {
            intro += Time.unscaledDeltaTime;
            yield return null;
        }
        for (int n = 3; n >= 1; n--)
        {
            bubble.Say("Starting in " + n + "...", true);
            float t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime; yield return null; }
        }

        float done = 0f;
        for (int trial = 0; trial < trials; trial++)
        {
            yield return Block(true, focusSeconds, total, done);
            done += focusSeconds;
            yield return Block(false, ignoreSeconds, total, done);
            done += ignoreSeconds;
        }

        // the end
        SetBlockVisuals(false, false);
        UpdateTimer(0f);
        bubble.Say("Great job! That was all I needed.");
        if (isTraining) BciUdp.Send("finish", Settings.MarkerPort);
        else BciUdp.Send("cal|done", Settings.ClassifierPort);
        float wait = 0f;
        while (wait < 2f) { wait += Time.unscaledDeltaTime; yield return null; }

        if (isTraining) BciFlow.AfterTraining();
        else BciFlow.AfterSession();
    }

    // One block. A pause throws the block away (marker 98) and it starts again from its beginning after RESUME.
    IEnumerator Block(bool focus, float seconds, float total, float doneBefore)
    {
        while (true)
        {
            while (paused) yield return null;
            SetBlockVisuals(true, !focus);
            bubble.Say(focus ? "Look at the flickering pad!" : "Now look at the dot and ignore the pad.");
            SendMarker(focus ? 1 : 2);

            float elapsed = 0f;
            bool interrupted = false;
            while (elapsed < seconds)
            {
                if (paused) { interrupted = true; break; }
                elapsed += Time.unscaledDeltaTime;
                UpdateTimer(total - doneBefore - elapsed);
                yield return null;
            }
            if (interrupted)
            {
                SendMarker(98);
                SetBlockVisuals(false, false);
                continue;
            }
            SendMarker(99);
            yield break;
        }
    }

    void SetBlockVisuals(bool padOn, bool dotOn)
    {
        if (padObject != null) padObject.SetActive(padOn);
        if (dotObject != null) dotObject.SetActive(dotOn);
    }

    void UpdateTimer(float remaining)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, remaining));
        timerLabel.text = $"{s / 60}:{s % 60:00}";
    }
}
