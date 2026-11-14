using System;
using System.IO;
using System.Text;
using UnityEngine;

// The profile of the player in front of the screen: who, which screen, and what follows from it (the pad frequency and size).
// It is written to ActiveProfile.json in the Documents folder, which every Python script reads (--profile).
//
// ProfileID  = Username_WIDTHxHEIGHT_REFRESHHz, e.g. Nada_1920x1080_60Hz. The same person on another screen (another refresh rate
//              means another pad frequency) is another profile with its own model.
[Serializable]
public class BciProfile
{
    public string ProfileID;
    public string Username;
    public bool IsNewUser;
    public int ScreenWidth;
    public int ScreenHeight;
    public float RefreshRate;           // Hz, as the operating system reports it (59.94 stays 59.94)
    public int FrameCount;              // screen frames in ONE full flicker cycle (always even: white for half, black for half)
    public float TrueFrequency;         // Hz = refresh / FrameCount, never in the alpha band or the epilepsy range
    public int PadDiameterSize;         // RoundToInt(7 * screenHeight / 1080), used as the pad's diameter in centimetres
    public float LineFrequency;         // mains frequency for the notch filter
    public string ModelsDir;
    public string DataDir;
    public string ModelPath;            // <ModelsDir>/<ProfileID>.joblib (exists once the training succeeded)
    public string FlagPath;             // <ModelsDir>/training_failed.flag (written by file 4 when the model is too weak)
    public string CreatedUtc;
    public bool DemoMode;               // the user name "test": the model is saved even when its accuracy is poor (file 4 reads this)

    // The demo user (case does not matter).
    public static bool IsDemoName(string name) => string.Equals((name ?? "").Trim(), "test", StringComparison.OrdinalIgnoreCase);

    // The profile of this run (set when the popup is confirmed).
    public static BciProfile Active { get; set; }

    // ---- where things are ----

    public static string DocumentsDir => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public static string ActiveProfilePath => Path.Combine(DocumentsDir, "ActiveProfile.json");
    public static string RootDir => Path.Combine(DocumentsDir, "BCIBusters");
    public static string ModelsDirPath => Path.Combine(RootDir, "models");
    public static string FailedFlagPath => Path.Combine(ModelsDirPath, "training_failed.flag");

    // ---- making a profile ----

    // Letters and digits only, so the name is safe in a file name and in a command line.
    public static string SanitizeName(string raw)
    {
        var sb = new StringBuilder();
        foreach (char c in (raw ?? "").Trim())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == ' ' || c == '_' || c == '-') sb.Append('_');
        }
        string s = sb.ToString().Trim('_');
        return s.Length > 24 ? s.Substring(0, 24) : s;
    }

    public static BciProfile Create(string username, bool isNewUser)
    {
        var resolution = Screen.currentResolution;
        float refresh = (float)resolution.refreshRateRatio.value;
        if (refresh < 20f) refresh = 60f;

        var profile = new BciProfile
        {
            Username = SanitizeName(username),
            IsNewUser = isNewUser,
            ScreenWidth = resolution.width,
            ScreenHeight = resolution.height,
            RefreshRate = refresh,
            DemoMode = IsDemoName(SanitizeName(username)),
            LineFrequency = BciSettings.Instance.LineFrequency,
            CreatedUtc = DateTime.UtcNow.ToString("o"),
        };
        ComputeFrequency(refresh, out profile.FrameCount, out profile.TrueFrequency);
        profile.PadDiameterSize = Mathf.RoundToInt(7f * ((float)resolution.height / 1080f));
        profile.ProfileID = $"{profile.Username}_{resolution.width}x{resolution.height}_{Mathf.RoundToInt(refresh)}Hz";
        profile.ModelsDir = ModelsDirPath;
        profile.DataDir = Path.Combine(Path.Combine(RootDir, "data"), profile.ProfileID);
        profile.ModelPath = Path.Combine(profile.ModelsDir, profile.ProfileID + ".joblib");
        profile.FlagPath = FailedFlagPath;
        return profile;
    }

    // The flicker frequency for this screen. The pad changes colour every FrameCount/2 frames, so the frequency is refresh / FrameCount.
    // Rules: FrameCount is an EVEN whole number that divides the refresh rate exactly (so every cycle is the same whole number of
    // frames, 50 % white and 50 % black), and the frequency is outside the alpha band (8-13 Hz) and the photosensitive epilepsy range
    // (15-25 Hz) - SsvepSettings.Allowed keeps out all of 8-25 Hz to be safe. Among the candidates the one in the middle of 5-8 Hz
    // wins (a strong response and far from both ranges): 60 Hz -> 10 frames -> 6 Hz, 144 Hz -> 24 frames -> 6 Hz, 120 Hz -> 20 -> 6 Hz.
    // A refresh rate with no even divisor (a few odd ones such as 75 Hz) falls back to the automatic choice of SsvepSettings.
    public static void ComputeFrequency(float refreshHz, out int frames, out float frequency)
    {
        int refresh = Mathf.RoundToInt(refreshHz);
        frames = 0;
        frequency = 0f;
        float bestScore = float.MaxValue;
        for (int n = 2; n <= refresh; n += 2)
        {
            if (refresh % n != 0) continue;
            float f = refresh / (float)n;
            if (f < 1f || !SsvepSettings.Allowed(f)) continue;
            float score = SsvepSettings.Score(f);
            if (score < bestScore)
            {
                bestScore = score;
                frames = n;
                frequency = f;
            }
        }
        if (frames > 0) return;

        var pad = SsvepSettings.Auto(refreshHz, 1)[0];
        frames = 2 * pad.halfCycleFrames;
        frequency = pad.frequency;
    }

    // ---- the files ----

    public void Save()
    {
        Directory.CreateDirectory(DocumentsDir);
        Directory.CreateDirectory(ModelsDir);
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(ActiveProfilePath, JsonUtility.ToJson(this, true));
        Debug.Log($"BCI profile written: {ActiveProfilePath}\n{JsonUtility.ToJson(this)}");
    }

    public bool ModelExists => File.Exists(ModelPath);

    // Has this user name a trained model on this computer (on any screen)? Used to refuse a "New User" that is already taken.
    public static bool UsernameTaken(string sanitizedName)
    {
        if (!Directory.Exists(ModelsDirPath)) return false;
        foreach (var file in Directory.GetFiles(ModelsDirPath, "*.joblib"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith("_FAILED_DEBUG", StringComparison.Ordinal)) continue;
            if (name.StartsWith(sanitizedName + "_", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Makes the pad of the SSVEP screens (calibration and the main level) follow this profile.
    public void ApplyToSsvep()
    {
        SsvepSettings.SetProfile(TrueFrequency, FrameCount / 2, PadDiameterSize);
    }
}
