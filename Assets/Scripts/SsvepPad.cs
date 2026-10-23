using UnityEngine;
using UnityEngine.UI;

// The SSVEP flickering pad (the component of the SsvepPad prefab, Assets/Resources/SsvepPad.prefab): a round pad with a crosshair
// in the middle for the user to look at. The pad and the crosshair swap between black and white, always in opposite colours:
// white pad with a black crosshair, then a black pad with a white crosshair.
//
// The flicker is locked to the screen's refreshes: it changes colour every `FramesPerHalfCycle` refreshes, so a full cycle takes
// twice that and the frequency is refresh rate / (2 x frames), exactly (a slow game frame is made up for, see DisplayFrame). Which numbers are allowed (not the alpha band, not the
// epilepsy range) and how they are worked out for the screen is in SsvepSettings.
//
// To use it: drop the prefab into a Canvas. By default it follows the frequencies of the SSVEP settings (padIndex 0: there is one pad). To set one yourself, untick Follow Settings and call Configure(frequency, framesPerHalfCycle).
[RequireComponent(typeof(RectTransform))]
public class SsvepPad : MonoBehaviour
{
    [SerializeField] Image pad;
    [SerializeField] Image crosshair;
    [Tooltip("Which of the settings' frequencies this pad uses (there is only one: 0).")]
    public int padIndex;
    [Tooltip("Use the frequencies of the SSVEP settings (main menu). Untick to set one yourself with Configure().")]
    public bool followSettings = true;

    public float Frequency { get; private set; }
    public int FramesPerHalfCycle { get; private set; } = 4;
    public bool White { get; private set; }                  // true while the pad is white (and the crosshair black)

    void OnEnable() => Refresh();

    // Take the pad's frequency from the settings again (after they changed, or after the screen's real rate was measured).
    public void Refresh()
    {
        if (!followSettings) return;
        var plan = SsvepSettings.Plan(SsvepSettings.PadCount);
        var mine = plan[Mathf.Clamp(padIndex, 0, plan.Length - 1)];
        Configure(mine.frequency, mine.halfCycleFrames);
    }

    public void Configure(float frequency, int framesPerHalfCycle)
    {
        Frequency = frequency;
        FramesPerHalfCycle = Mathf.Max(1, framesPerHalfCycle);
    }

    // The number of screen refreshes since the game started, as exactly as the game can know it: one per game frame, plus the
    // refreshes that went by while a slow game frame was being made. Every pad shares it, so they all start together.
    static long displayFrame;
    static int countedFrame = -1;

    static long DisplayFrame()
    {
        if (countedFrame != Time.frameCount)
        {
            if (countedFrame >= 0)
                displayFrame += Mathf.Max(1, Mathf.RoundToInt(Time.unscaledDeltaTime * SsvepSettings.RefreshForPlanning));
            countedFrame = Time.frameCount;
        }
        return displayFrame;
    }

    void Update()
    {
        // Screen refresh number / refreshes per half cycle: even = one colour, odd = the other.
        White = (DisplayFrame() / FramesPerHalfCycle) % 2 == 0;
        if (pad != null) pad.color = White ? Color.white : Color.black;
        if (crosshair != null) crosshair.color = White ? Color.black : Color.white;       // always the opposite of the pad
    }
}
