using UnityEngine;

// The settings of the BCI flow (profile popup -> contrast -> headset check -> calibration -> training -> main level), in one asset
// (Assets/Resources/BciSettings.asset, made by BciSetup when Unity opens the project). Select it in the Project window to edit it
// in the Inspector - above all PythonInterpreterPath.
public class BciSettings : ScriptableObject
{
    [Header("Python")]
    [Tooltip("The Python that runs the scripts, e.g. C:/python/python.exe. 'python' = the first one on the PATH. It needs numpy, scipy, " +
             "pandas, scikit-learn, joblib, pylsl and brainflow.")]
    public string PythonInterpreterPath = "python";
    [Tooltip("The folder with the Python scripts. Empty = Assets/StreamingAssets/Python.")]
    public string ScriptsFolder = "";
    [Tooltip("Passed to file 1 as --simulate: fake EEG, to try the whole flow without the headset.")]
    public bool SimulateHeadset = false;
    [Tooltip("Passed to file 1 as --sim-bad (only with Simulate Headset), e.g. Pz=flat,Oz=noisy: some electrodes bad, to see the colours.")]
    public string SimulatedBadElectrodes = "";
    [Tooltip("The Unicorn serial number given to file 1 as --serial.")]
    public string HeadsetSerial = "UN-2022.03.74";
    [Tooltip("Mains frequency of the country (50 Europe / Africa / Asia, 60 Americas).")]
    public float LineFrequency = 50f;

    [Header("Scenes")]
    [Tooltip("The scene of the game itself (the 'MainLevel').")]
    public string MainLevelScene = "GameScene";
    [Tooltip("Tick to skip the whole BCI flow: PLAY loads the game at once (for working on the game without the headset).")]
    public bool SkipBciFlow = false;
    [Tooltip("The monitor-contrast screen (a dark grey box, RGB 5,5,5, on black). OFF for now: the box did not show up on the player's monitor. " +
             "The code and the BciContrast scene are still there; tick this to bring the screen back before the headset check.")]
    public bool ShowContrastScreen = false;

    [Header("Calibration (a trial = PREPARE + FLICKER + REST, the same sequence as the lab's Python paradigm)")]
    [Tooltip("The pad's outline and the crosshair are shown, nothing flickers yet: the player gets ready.")]
    public float PrepareSeconds = 2f;
    [Tooltip("OFF (default): the Prepare phase comes ONCE, right before the first flicker; afterwards the trials are FLICKER + REST only. " +
             "ON: a Prepare phase before every flicker (the lab paradigm's cue).")]
    public bool PrepareEveryTrial = false;
    [Tooltip("Seconds of calm after the scripts are ready, before the Prepare phase: the player reads the instruction. The EEG of this time " +
             "(no flicker) is also used to test the model (false alarms).")]
    public float IntroSeconds = 10f;
    [Tooltip("The pad flickers and the player looks at the crosshair in its middle. This is the ONE class the classifier learns (4 s = one epoch).")]
    public float FocusSeconds = 4f;
    [Tooltip("The pad is gone, only the crosshair stays: the player relaxes.")]
    public float RestSeconds = 2f;
    [Tooltip("TrainingCalibration (new user): the time the on-screen timer counts down. The number of trials follows from it " +
             "(120 s = 20 trials of flicker 4 s + rest 2 s). The one Prepare before the first flicker is not counted.")]
    public float TrainingSeconds = 120f;
    [Tooltip("SessionCalibration (old user): the same, for the short warm-up (30 s = 5 trials).")]
    public float SessionSeconds = 30f;

    [Header("Ports (localhost)")]
    public int StatusPort = 5010;        // Unity listens: electrode status from file 1
    public int ControlPort = 5011;       // file 1 listens: quit
    public int MarkerPort = 5007;        // file 2 listens: markers from Unity
    public int ClassifierPort = 5008;    // bci_game.py listens: pad on/off + session calibration from Unity
    public int GamePort = 5005;          // Unity listens (BciListener): ping / switch from bci_game.py

    [Header("Look")]
    [Tooltip("The character who speaks in the speech bubbles. Empty = Tenna (Assets/deltarune, through BciArt).")]
    public Sprite CharacterSprite;

    static BciSettings instance;

    public static BciSettings Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<BciSettings>("BciSettings");
            if (instance == null) instance = CreateInstance<BciSettings>();        // not made yet: the defaults
            return instance;
        }
    }
}
