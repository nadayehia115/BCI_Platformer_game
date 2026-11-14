using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// The route through the BCI stages, and what starts when:
//
//   main menu PLAY -> profile popup (BciProfileManager)
//   (Stage 1 BciContrast          the monitor-brightness screen: OFF for now, BciSettings.ShowContrastScreen brings it back)
//   Stage 2  BciHeadsetCheck      file 1 (headset streamer) starts silently; the 8 electrodes turn green (colours come from file 1 by UDP)
//   Stage 3  New user: TrainingCalibration  (file 2 records)      Old user: SessionCalibration  (the live classifier is started)
//   Stage 4  New user: BciLoading           (file 3 -> file 4 train the model, a mini-game while waiting)
//   then     MainLevel = the game scene (BciSettings.MainLevelScene); the live classifier (bci_game.py) runs there.
//
// Esc on any stage (or EXIT) calls Abort(): every Python script is stopped and the main menu comes back.
public static class BciFlow
{
    public const string ContrastScene = "BciContrast";
    public const string HeadsetScene = "BciHeadsetCheck";
    public const string TrainingScene = "TrainingCalibration";
    public const string SessionScene = "SessionCalibration";
    public const string LoadingScene = "BciLoading";
    public const string MenuScene = "MainMenu";

    // Tags of the scripts in BciProcessManager
    public const string HeadsetTag = "headset", RecorderTag = "recorder", ExtractTag = "features", TrainTag = "model", ClassifierTag = "classifier";

    static BciSettings Settings => BciSettings.Instance;

    public static string Quote(string path) => "\"" + path + "\"";

    // The player confirmed the popup: the profile is saved, the headset script starts and Stage 1 loads.
    public static void Begin(BciProfile profile)
    {
        BciProfile.Active = profile;
        TrainingSkipped = false;
        profile.Save();
        profile.ApplyToSsvep();
        if (profile.IsNewUser && File.Exists(profile.FlagPath)) File.Delete(profile.FlagPath);   // an old failure flag must not misdirect the new run
        // A new user never has a model yet (the popup refuses a taken name) - except the demo user "test", whose old model is replaced; the loading
        // screen would take it for the NEW one.
        if (profile.IsNewUser && File.Exists(profile.ModelPath)) File.Delete(profile.ModelPath);

        var manager = BciProcessManager.Instance;
        manager.StartStatusListener();
        StartHeadset();
        SceneManager.LoadScene(Settings.ShowContrastScreen ? ContrastScene : HeadsetScene);
    }

    // A stage scene played on its own (no popup before it): make a profile and start the headset script, so the scene has what it needs.
    public static void EnsureProfile()
    {
        if (BciProfile.Active != null) return;
        var profile = BciProfile.Create("DebugUser", true);
        BciProfile.Active = profile;
        profile.Save();
        profile.ApplyToSsvep();
        BciProcessManager.Instance.StartStatusListener();
        if (!BciProcessManager.Instance.IsRunning(HeadsetTag)) StartHeadset();
        Debug.Log("BCI: no profile yet (stage scene played directly): using a debug profile.");
    }

    // (Re)starts file 1 with the headset options of BciSettings.
    public static void StartHeadset()
    {
        var s = Settings;
        string args = $"--serial {Quote(s.HeadsetSerial)} --line-freq {s.LineFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                      $"--status-port {s.StatusPort} --control-port {s.ControlPort}";
        if (s.SimulateHeadset)
        {
            args += " --simulate";
            if (!string.IsNullOrWhiteSpace(s.SimulatedBadElectrodes)) args += $" --sim-bad {Quote(s.SimulatedBadElectrodes)}";
        }
        BciProcessManager.Instance.Launch("1-unicorn_steamer_hardware.py", args, HeadsetTag, s.ControlPort);
    }

    public static void AfterContrast() => SceneManager.LoadScene(HeadsetScene);

    // Stage 2 done: the calibration of this kind of user.
    public static void AfterHeadsetCheck()
    {
        var p = BciProfile.Active;
        bool trained = p != null && p.ModelExists;
        // New user (or an old one whose model is gone): the long training. Old user with a model: the short session calibration.
        SceneManager.LoadScene(p != null && p.IsNewUser || !trained ? TrainingScene : SessionScene);
    }

    public static string CalibrationSceneForUser() =>
        BciProfile.Active != null && BciProfile.Active.IsNewUser ? TrainingScene : SessionScene;

    public static void AfterTraining()
    {
        TrainingSkipped = false;
        SceneManager.LoadScene(LoadingScene);
    }

    public static void AfterSession() => LoadMainLevel();

    // The live classifier runs from here on (it listens to the EEG and waits for the pad).
    public static void StartClassifier()
    {
        var p = BciProfile.Active;
        if (p == null || BciProcessManager.Instance.IsRunning(ClassifierTag)) return;
        BciProcessManager.Instance.Launch("bci_game.py", $"--profile {Quote(BciProfile.ActiveProfilePath)}", ClassifierTag, Settings.ClassifierPort);
    }

    public static void LoadMainLevel()
    {
        var p = BciProfile.Active;
        if (p != null && p.ModelExists) StartClassifier();
        else Debug.Log("BCI: no trained model for this profile, so no live classifier: use the T key for the power (demo).");
        SceneManager.LoadScene(Settings.MainLevelScene);
    }

    // DEMO: the letter O skips ONE screen at a time, when the player wants it.
    //   TrainingCalibration (new user): the recording is cut short and the loading screen comes next, WITHOUT training (TrainingSkipped):
    //                                   its warm-up game stays until the player presses O once more;
    //   SessionCalibration (old user):  the level (the saved model is used as it is);
    //   BciLoading:                     the level (a model that is not ready yet is dropped).
    // A new user who skipped has no model, so the live classifier does not start and the T key stands in for it.
    public static bool TrainingSkipped;

    public static void SkipCalibration()
    {
        bool training = SceneManager.GetActiveScene().name == TrainingScene;
        Debug.Log("BCI: O pressed - calibration skipped (demo)" + (training ? ": the loading screen comes next, without training." : "."));
        if (training)
        {
            TrainingSkipped = true;
            SceneManager.LoadScene(LoadingScene);
        }
        else LoadMainLevel();
    }

    public static void RetryCalibration()
    {
        TrainingSkipped = false;
        SceneManager.LoadScene(CalibrationSceneForUser());
    }

    // Leave the flow: no script keeps running, the main menu comes back.
    public static void Abort()
    {
        BciProcessManager.Instance.KillAll();
        BciProfile.Active = null;
        SsvepSettings.ClearProfile();
        Time.timeScale = 1f;
        SceneManager.LoadScene(MenuScene);
    }
}
