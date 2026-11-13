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

    [Header("Calibration (a trial = one FOCUS block + one IGNORE block)")]
    public float FocusSeconds = 5f;
    public float IgnoreSeconds = 5f;
    [Tooltip("TrainingCalibration (new user): 12 trials = 2 minutes.")]
    public int TrainingTrials = 12;
    [Tooltip("SessionCalibration (old user): 3 trials = 30 seconds.")]
    public int SessionTrials = 3;

    [Header("Ports (localhost)")]
    public int StatusPort = 5010;        // Unity listens: electrode status from file 1
    public int ControlPort = 5011;       // file 1 listens: quit
    public int MarkerPort = 5007;        // file 2 listens: markers from Unity
    public int ClassifierPort = 5008;    // bci_game.py listens: pad on/off + session calibration from Unity
    public int GamePort = 5005;          // Unity listens (BciListener): ping / switch from bci_game.py

    [Header("Look")]
    [Tooltip("The character who speaks in the speech bubbles. Empty = Ralsei until you give a sprite.")]
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
