using System.Linq;
using UnityEngine;

// The sprites of the dark fountain sequence, gathered in one asset (Assets/Resources/FountainArt.asset) so the game can load
// them by name at runtime. The asset is made by FountainArtSetup (it runs by itself when Unity opens the project; the menu
// Tools > Setup Fountain Art redoes it). The pictures themselves stay in Assets/deltarune.
public class FountainArt : ScriptableObject
{
    public Sprite[] making;          // Makingfountain_0..22: Kris making the fountain (silhouettes, red eyes, white silhouette, cracks)
    public Sprite[] krisTransform;    // Kris_transform_0..26: frames 14-26 are the blue Kris tumbling out of the smoke (0-13 are not used)
    public Sprite[] sparkles;         // Fountain_opening_eff_0..2: the twinkling stars

    // The pictures have empty rows under the feet (3-5 pixels). So that Kris stands on the floor and not above it, the setup
    // measures them once: padSprites[i] has pads[i] world units of empty space at the bottom.
    public Sprite[] padSprites;
    public float[] pads;

    public bool Complete =>
        Has(making, 23) && Has(krisTransform, 27) && Has(sparkles, 3)
        && padSprites != null && pads != null && pads.Length == padSprites.Length && pads.Length >= 53;

    // The empty space (world units) under the feet in this picture; 0 if it is not one of the measured ones.
    public float PadOf(Sprite sprite)
    {
        if (padSprites == null || sprite == null) return 0f;
        for (int i = 0; i < padSprites.Length; i++)
            if (padSprites[i] == sprite) return pads[i];
        return 0f;
    }

    static bool Has(Sprite[] sprites, int count) => sprites != null && sprites.Length >= count && sprites.All(s => s != null);
}
