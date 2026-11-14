using System.Collections;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Stage 4: the interactive loading screen. While the model is trained behind the scenes, the player warms up with a small endless
// runner (jump over the crates with Space / W / Up).
//
//   1. file 2 (the recorder) finishes saving the calibration CSV,
//   2. 3-unicorn_offline_feature_extraction.py runs, then 4-unicorn_offline_classification_model.py (one after the other),
//   3. a coroutine watches the models folder:
//        training_failed.flag exists            -> the data was too noisy: say so, wait 3 s, delete the flag, calibrate again
//        <ProfileID>.joblib exists (no flag)    -> "Model saved! Let's go!", wait 1.5 s, load the main level
//      (and if both scripts are over and neither file appeared, something broke: the same retry, but only twice - then the
//      character asks the player to look at the Console / the Python path instead of looping forever.)
public class BciLoadingStage : BciStage
{
    const string WarmUpText = "Here... warm up your fingers while we unlock your mind control abilities.";
    static int internalFailures;

    BciSpeechBubble bubble;
    BciRunnerGame game;
    Process extractProcess, trainProcess;
    bool trainingScriptsDone;                 // both Python scripts have run (or could not start)

    protected override void BuildScreen(Transform root)
    {
        game = BciRunnerGame.Create(root, -230f);
        bubble = BciSpeechBubble.Create(root, new Vector2(0f, 1f), new Vector2(50f, -40f), 1000f);
        bubble.Say(WarmUpText);
        StartCoroutine(Train());
        StartCoroutine(Watch());
    }

    // ---- the Python side ----

    IEnumerator Train()
    {
        var manager = BciProcessManager.Instance;
        string profileArg = $"--profile {BciFlow.Quote(BciProfile.ActiveProfilePath)}";

        // 1. the recorder saves its CSV when it has heard "finish" (the calibration scene sent it); wait for it (at most 30 s)
        float waited = 0f;
        while (manager.IsRunning(BciFlow.RecorderTag) && waited < 30f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (manager.IsRunning(BciFlow.RecorderTag))
        {
            Debug.LogWarning("BCI: the recorder did not finish in 30 s; stopping it.");
            manager.Stop(BciFlow.RecorderTag, false);
        }

        // 2. features, then the model
        extractProcess = manager.Launch("3-unicorn_offline_feature_extraction.py", profileArg, BciFlow.ExtractTag);
        yield return WaitForExit(extractProcess);
        trainProcess = manager.Launch("4-unicorn_offline_classification_model.py", profileArg, BciFlow.TrainTag);
        yield return WaitForExit(trainProcess);
        trainingScriptsDone = true;
    }

    static IEnumerator WaitForExit(Process process)
    {
        if (process == null) yield break;                                   // could not start: the watcher notices
        while (true)
        {
            bool exited;
            try { exited = process.HasExited; } catch { exited = true; }
            if (exited) yield break;
            yield return null;
        }
    }

    // ---- watching the models folder ----

    IEnumerator Watch()
    {
        var profile = BciProfile.Active;
        float finishedAt = -1f;
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.5f);

            // the flag is checked FIRST: a failed training also leaves a *_FAILED_DEBUG.joblib, never <ProfileID>.joblib
            if (File.Exists(profile.FlagPath))
            {
                game.Freeze();
                bubble.Say("The brainwave data was too noisy! We need to generate a new one. Let's try the calibration again.");
                yield return new WaitForSecondsRealtime(3f);
                try { File.Delete(profile.FlagPath); } catch (System.Exception e) { Debug.LogWarning("BCI: could not delete the flag: " + e.Message); }
                BciFlow.RetryCalibration();
                yield break;
            }
            if (File.Exists(profile.ModelPath))
            {
                game.Freeze();
                bubble.SetPose(BciSpeechBubble.Pose.Laugh);
                bubble.Say("Model saved! Let's go!");
                yield return new WaitForSecondsRealtime(1.5f);
                BciFlow.LoadMainLevel();
                yield break;
            }

            // both scripts over and no result: something broke (Python missing, a crash ...)
            if (trainingScriptsDone)
            {
                if (finishedAt < 0f) finishedAt = Time.unscaledTime;
                if (Time.unscaledTime - finishedAt > 3f)
                {
                    game.Freeze();
                    internalFailures++;
                    if (internalFailures >= 3)
                    {
                        bubble.Say("Something went wrong while I was learning from your brainwaves. Press Esc, then check the Unity Console " +
                                   "and the Python path in BciSettings.");
                        yield break;
                    }
                    bubble.Say("Oops, I could not finish learning from your brainwaves. Let's try the calibration again.");
                    yield return new WaitForSecondsRealtime(3f);
                    BciFlow.RetryCalibration();
                    yield break;
                }
            }
        }
    }

    protected override void Update()
    {
        base.Update();
    }
}
