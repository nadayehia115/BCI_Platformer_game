using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The SSVEP pad in the game: ONE flickering pad (the SsvepPad prefab, one frequency) in the middle of the screen (left to right),
// raised so that it floats just above Kris's head and does not hide him. It means
// "activate power": when the BCI program finds from the brain signal that the player keeps looking at it, it sends the Python
// T and the game brings in the friend the obstacle needs by itself (CharacterSwitcher.AutoSelect).
//
// The pad is a real size on the screen, 3 cm across by default (a choice in the main menu SETTINGS, see SsvepSettings.PadCm).
//
// It is NOT shown until the player presses S at an obstacle and the dark fountain has played there (KrisForms): then it fades in
// and stays while an obstacle that needs a friend is on the screen (PowerTargets). The power is activated by the Python T, which
// uses the pad up: it fades out and stays away (a rest, like the stretches between obstacles) until the next S.
//
// Two jobs:
//   * Create(switcher)  puts the pad on the HUD. KrisForms calls it when the dark fountain is done.
//   * the calibrator    runs once when the game starts: asks for one game frame per screen refresh (vSync), then measures how fast
//                       the frames really come. The frequencies are planned for the refresh rate the screen reports; only if the
//                       frames come at a different but steady rate (the screen really runs at that rate) is that rate used.
// The frequencies come from SsvepSettings (automatic for this screen, or the ones chosen in the main menu).
public class SsvepBoard : MonoBehaviour
{
    const string PrefabResource = "SsvepPad";
    const float PrefabDiameter = 200f;             // the pad disc of the prefab is 200 units across
    const int WarmUpFrames = 45, MeasureFrames = 120;
    const float FadeSeconds = 0.5f;

    public static SsvepBoard Instance { get; private set; }
    public static bool Calibrated { get; private set; }

    SsvepPad pad;
    RectTransform padRect;
    PlayerController kris;
    Collider2D krisCollider;
    Canvas canvas;
    CanvasGroup group;
    bool wanted;                                   // an obstacle that needs a power is near
    float nextCheck;

    // The frequency (Hz) of the pad (a list of one), as it is on the screen right now.
    public static float[] Frequencies => SsvepSettings.Frequencies();

    // ---- start-up: vSync and measuring the real frame rate ----

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        // Flicker is counted in screen frames, so every game frame has to be one screen frame: vSync on, no frame rate cap.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;
        if (Calibrated || FindFirstObjectByType<SsvepCalibrator>() != null) return;
        var go = new GameObject("SSVEP Calibrator");
        DontDestroyOnLoad(go);
        go.AddComponent<SsvepCalibrator>();
    }

    // Measures the frame rate for a couple of seconds after the game starts, and tells SsvepSettings.
    class SsvepCalibrator : MonoBehaviour
    {
        IEnumerator Start()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < WarmUpFrames; i++) yield return null;          // loading hiccups do not count
                var times = new List<float>(MeasureFrames);
                for (int i = 0; i < MeasureFrames; i++)
                {
                    yield return null;
                    times.Add(Time.unscaledDeltaTime);
                }
                times.Sort();
                float median = times[times.Count / 2];                              // the median ignores the odd slow frame
                float measured = median > 0f ? 1f / median : 0f;
                float screen = SsvepSettings.ScreenRefresh;

                if (measured > screen * 1.05f && pass == 0)
                {
                    // Frames come faster than the screen can show them: vSync is not working here (for example the editor's
                    // Game view with VSync off). Cap the game at the screen's rate and measure again.
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = Mathf.RoundToInt(screen);
                    continue;
                }

                // How steady are the frames? (the share of frames within 10 % of the median)
                int steady = 0;
                foreach (float t in times) if (Mathf.Abs(t - median) <= median * 0.1f) steady++;
                bool stable = steady >= times.Count * 0.85f;

                // Within 5 % of what the screen says: trust the screen's exact number (59.94 and the like). A different but
                // steady rate means the screen really runs at that rate (the reported one is wrong). Anything else is the game
                // being slow for a moment: the screen's number stays, and the pads count the frames that were skipped.
                bool close = Mathf.Abs(measured - screen) <= screen * 0.05f;
                SsvepSettings.MeasuredRefresh = !close && stable && measured >= 20f ? SsvepSettings.StandardRate(measured) : 0f;
                Debug.Log($"SSVEP: the screen says {screen:0.##} Hz, the game measured {measured:0.#} frames per second ({(stable ? "steady" : "not steady")}); pads at " +
                          string.Join(" / ", System.Array.ConvertAll(Frequencies, f => f.ToString("0.0#") + " Hz")) +
                          (SsvepSettings.Manual ? " (chosen by hand)." : " (worked out automatically)."));
                if (SsvepSettings.MeasuredRefresh > 0f)
                    Debug.LogWarning($"SSVEP: the game steadily runs at {measured:0.#} frames per second, not at the {screen:0.#} Hz the screen reports, " +
                                     "so the frequencies are worked out for " + SsvepSettings.MeasuredRefresh + " Hz.");
                else if (!close)
                    Debug.LogWarning($"SSVEP: the game runs at only {measured:0.#} frames per second on a {screen:0.#} Hz screen (the editor, or a busy PC). " +
                                     "The pads keep their rate by counting the skipped frames, but the flicker can look uneven. A built game runs smoother.");
                break;
            }
            Calibrated = true;
            if (Instance != null) Instance.RefreshPads();
            Destroy(gameObject);
        }
    }

    // ---- the pad on the HUD ----

    // The pad in the middle of the screen. Call once; calling again does nothing.
    public static void Create(CharacterSwitcher switcher)
    {
        if (Instance != null || switcher == null) return;
        var prefab = Resources.Load<GameObject>(PrefabResource);
        if (prefab == null)
        {
            Debug.LogWarning("SSVEP: Assets/Resources/SsvepPad.prefab is missing (Tools > Setup SSVEP Pad makes it).");
            return;
        }
        var hudCanvas = switcher.GetComponentInParent<Canvas>();
        var parent = hudCanvas != null ? hudCanvas.rootCanvas.transform : switcher.transform;
        var host = new GameObject("SSVEP Board", typeof(RectTransform), typeof(CanvasGroup));
        host.transform.SetParent(parent, false);
        var rect = (RectTransform)host.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        host.AddComponent<SsvepBoard>().Build(prefab);
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build(GameObject prefab)
    {
        group = GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.alpha = 0f;
        canvas = GetComponentInParent<Canvas>().rootCanvas;
        kris = FindFirstObjectByType<PlayerController>();
        krisCollider = kris != null ? kris.GetComponent<Collider2D>() : null;

        var go = Instantiate(prefab, transform);
        go.name = "SSVEP Pad";
        padRect = (RectTransform)go.transform;
        padRect.anchorMin = padRect.anchorMax = new Vector2(0.5f, 0.5f);       // the middle of the screen
        pad = go.GetComponent<SsvepPad>();
        pad.padIndex = 0;
        pad.followSettings = true;
        pad.Refresh();

        ApplySize();
        RefreshPads();
        Debug.Log($"SSVEP pad is on the screen: {pad.Frequency:0.0#} Hz, {SsvepSettings.PadCm:0.#} cm across ({SsvepSettings.PadPixels:0} px at {SsvepSettings.Dpi:0} dpi).");
    }

    // The pad has a real size (centimetres): pixels on this screen, then canvas units (the canvas is scaled to the screen). It stays
    // in the middle of the screen from left to right, and is raised so that its lower edge floats a little above Kris's head (the
    // camera keeps Kris in the middle of the screen). A very large pad is kept on the screen, even if it then covers him.
    void ApplySize()
    {
        float scale = Mathf.Max(0.01f, canvas.scaleFactor);
        float diameter = SsvepSettings.PadPixels / scale;
        padRect.localScale = Vector3.one * (diameter / PrefabDiameter);

        float headUnits = 1.1f;                                                       // Kris's head above the middle of the screen (world units)
        if (kris != null && krisCollider != null) headUnits = krisCollider.bounds.max.y - kris.transform.position.y;
        var cam = Camera.main;
        float pixelsPerUnit = cam != null && cam.orthographic ? Screen.height / (2f * cam.orthographicSize) : 77f;
        float headPixels = (headUnits + 0.25f) * pixelsPerUnit;                        // a little air above the hair
        float gapPixels = 0.3f / 2.54f * SsvepSettings.Dpi;                            // 3 mm between his head and the pad

        float rise = (headPixels + gapPixels) / scale + diameter / 2f;                // the middle of the pad above the middle of the screen
        float highest = Screen.height / scale / 2f - diameter / 2f - 10f;             // keep the pad on the screen
        padRect.anchoredPosition = new Vector2(0f, Mathf.Min(rise, Mathf.Max(0f, highest)));
    }

    // Fades the pad in while an obstacle that needs a friend is near, and out when there is none.
    void Update()
    {
        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + 0.2f;
            wanted = KrisForms.PowersUnlocked && PowerTargets.Nearest(true, 0f, out _);
            ApplySize();                                                            // the window may have been resized
        }
        group.alpha = Mathf.MoveTowards(group.alpha, wanted ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
    }

    // Takes the frequency from the settings again (after they changed, or after the screen's real rate was measured).
    public void RefreshPads()
    {
        if (pad != null) pad.Refresh();
    }
}
