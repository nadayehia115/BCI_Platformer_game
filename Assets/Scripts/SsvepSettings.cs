using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Which frequencies the SSVEP pads flicker at, worked out from the screen the game is running on, or chosen by hand in the main
// menu (the SSVEP tab).
//
// The rules a frequency has to follow:
//   1. NOT in the alpha band (8-14 Hz): the brain's own rhythm there would be mistaken for the response to a pad.
//   2. NOT in the photosensitive epilepsy range (15-25 Hz): the most dangerous flicker for people with epilepsy.
//   3. It has to fit the screen's refresh rate EXACTLY. A screen can only change a whole number of times per second, so the
//      pad is switched every n whole frames: a full cycle (white, then black) takes 2n frames and the frequency is
//      refresh rate / (2n). On a 60 Hz screen n = 4 gives 7.5 Hz, n = 5 gives 6 Hz and n = 6 gives 5 Hz; 10 Hz (n = 3) and
//      15 Hz (n = 2) are not allowed. On a 120 or 144 Hz screen other values appear, so it is worked out for each machine.
//
// Allowed values below 8 Hz are preferred (around 5 to 8 Hz the response is strong); above 25 Hz is the second choice.
public static class SsvepSettings
{
    public const float ForbiddenLow = 8f, ForbiddenHigh = 25f;       // 8-14 Hz alpha band + 15-25 Hz epilepsy range
    public const int PadCount = 1;                                    // one pad: "activate power" (the game knows which friend by itself)
    public static readonly string[] PadNames = { "ACTIVATE POWER" };

    const string ManualKey = "SsvepManual", FrequencyKey = "SsvepFrequency";
    const string SizeKey = "SsvepPadCm";

    // ---- the size of the pad: a real size on the screen (centimetres), so it is the same on every monitor ----

    public const float DefaultPadCm = 3f, MinPadCm = 1f, MaxPadCm = 10f;

    public static float PadCm
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(SizeKey, DefaultPadCm), MinPadCm, MaxPadCm);
        set
        {
            PlayerPrefs.SetFloat(SizeKey, Mathf.Clamp(value, MinPadCm, MaxPadCm));
            PlayerPrefs.Save();
        }
    }

    // Pixels per inch of the screen, as the operating system says (96 if it says nothing).
    public static float Dpi => Screen.dpi > 20f ? Screen.dpi : 96f;

    // How many screen pixels the pad is across.
    public static float PadPixels => PadCm / 2.54f * Dpi;

    public struct Pad
    {
        public float frequency;           // Hz
        public int halfCycleFrames;       // the pad changes colour every this many frames
    }

    public static bool Allowed(float hz) => hz < ForbiddenLow || hz > ForbiddenHigh;

    // ---- the screen ----

    // The refresh rate the game measured while it was running (see SsvepBoard); 0 = not measured, use what the screen reports.
    public static float MeasuredRefresh { get; set; }

    // What the operating system says the screen refreshes at (Hz); 60 if it does not say.
    public static float ScreenRefresh
    {
        get
        {
            float hz = (float)Screen.currentResolution.refreshRateRatio.value;
            return hz >= 20f ? hz : 60f;
        }
    }

    public static float RefreshForPlanning => MeasuredRefresh > 0f ? MeasuredRefresh : ScreenRefresh;

    // A measured frame rate snapped to the nearest common screen rate (within 3 %), otherwise just rounded.
    public static float StandardRate(float measured)
    {
        foreach (float rate in new[] { 24f, 25f, 30f, 48f, 50f, 60f, 72f, 75f, 90f, 100f, 120f, 144f, 165f, 180f, 240f })
            if (Mathf.Abs(measured - rate) <= rate * 0.03f) return rate;
        return Mathf.Round(measured);
    }

    // ---- automatic ----

    public static Pad[] Auto(float refresh, int count)
    {
        var candidates = new List<Pad>();
        for (int n = 1; n <= 400; n++)
        {
            float f = refresh / (2f * n);
            if (f < 1f) break;
            if (Allowed(f)) candidates.Add(new Pad { frequency = f, halfCycleFrames = n });
        }
        candidates.Sort((a, b) => Score(a.frequency).CompareTo(Score(b.frequency)));

        var chosen = new List<Pad>();
        foreach (var c in candidates)                                          // the best ones that do not clash with each other
        {
            if (chosen.Count == count) break;
            if (chosen.All(x => Distinct(x.frequency, c.frequency))) chosen.Add(c);
        }
        foreach (var c in candidates)                                          // a very slow screen: take what is left
        {
            if (chosen.Count == count) break;
            if (!chosen.Any(x => x.halfCycleFrames == c.halfCycleFrames)) chosen.Add(c);
        }
        return chosen.OrderBy(p => p.frequency).ToArray();                     // slot order: lowest frequency first
    }

    // Lower is better: the middle of 5-8 Hz first, then 3-5 Hz, then above 25 Hz, then anything slower.
    static float Score(float f)
    {
        if (f >= 5f && f < ForbiddenLow) return Mathf.Abs(f - 6.5f);
        if (f >= 3f && f < 5f) return 10f + (5f - f);
        if (f > ForbiddenHigh) return 20f + (f - ForbiddenHigh) * 0.01f;
        return 40f + (3f - f);
    }

    // Two pads must be told apart: not too close, and neither a double nor a triple of the other (harmonics).
    public static bool Distinct(float a, float b)
    {
        if (Mathf.Abs(a - b) < 0.5f) return false;
        for (int k = 2; k <= 3; k++)
            if (Mathf.Abs(a - k * b) < 0.3f || Mathf.Abs(b - k * a) < 0.3f) return false;
        return true;
    }

    // ---- chosen by hand ----

    public static bool Manual
    {
        get => PlayerPrefs.GetInt(ManualKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(ManualKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // The nearest allowed frequency this screen can really show (refresh / 2n) to the one asked for.
    public static Pad Snap(float wanted, float refresh)
    {
        var best = new Pad { frequency = wanted, halfCycleFrames = 1 };
        float bestDistance = float.MaxValue;
        for (int n = 1; n <= 400; n++)
        {
            float f = refresh / (2f * n);
            if (f < 1f) break;
            if (!Allowed(f)) continue;
            float d = Mathf.Abs(f - wanted);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = new Pad { frequency = f, halfCycleFrames = n };
            }
        }
        return best;
    }

    // What was typed for pad i (0 = nothing typed yet).
    public static float ManualWanted(int index) => PlayerPrefs.GetFloat(FrequencyKey + index, 0f);

    public static void SetManualWanted(int index, float hz)
    {
        PlayerPrefs.SetFloat(FrequencyKey + index, hz);
        PlayerPrefs.Save();
    }

    // ---- what the game uses ----

    // The pads for this screen: the hand-chosen frequencies (snapped to what the screen can show) or the automatic ones.
    public static Pad[] Plan(int count)
    {
        float refresh = RefreshForPlanning;
        var auto = Auto(refresh, count);
        if (!Manual) return auto;

        var pads = new Pad[count];
        for (int i = 0; i < count; i++)
        {
            float wanted = ManualWanted(i);
            pads[i] = wanted > 0f ? Snap(wanted, refresh) : auto[Mathf.Min(i, auto.Length - 1)];
        }
        return pads;
    }

    public static float[] Frequencies() => Plan(PadCount).Select(p => p.frequency).ToArray();
}
