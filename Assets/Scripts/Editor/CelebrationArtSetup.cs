using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds Assets/Resources/CelebrationArt.asset (the pictures of the four characters for the level won dance). Like
// FountainArtSetup it runs by itself after Unity compiles / opens the project whenever the asset is missing or incomplete;
// Tools > Setup Celebration Art does it on demand.
public static class CelebrationArtSetup
{
    const string AssetPath = "Assets/Resources/CelebrationArt.asset";
    const string Chars = "Assets/Characters/";

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var art = AssetDatabase.LoadAssetAtPath<CelebrationArt>(AssetPath);
            if (art == null || !art.Complete || !art.kris.HasDance || !art.susie.HasDance || !art.ralsei.HasDance) Build(false);
        };
    }

    [MenuItem("Tools/Setup Celebration Art")]
    static void Menu() => Build(true);

    static void Build(bool report)
    {
        var art = AssetDatabase.LoadAssetAtPath<CelebrationArt>(AssetPath);
        bool isNew = art == null;
        if (isNew) art = ScriptableObject.CreateInstance<CelebrationArt>();

        // The dance frames of Kris, Susie and Ralsei (cut out of the pictures the user sent).
        art.kris = new CelebrationArt.DanceSet { dance = Load(Chars + "Dance/", "Kris_dance_", 0, 3) };
        art.susie = new CelebrationArt.DanceSet { dance = Load(Chars + "Dance/", "Susie_dance_", 0, 9) };
        art.ralsei = new CelebrationArt.DanceSet { dance = Load(Chars + "Dance/", "Ralsei_dance_", 0, 7) };
        // Noelle has none: her dance is made in code from her own pictures.
        art.noelle = new CelebrationArt.DanceSet
        {
            idle = Load(Chars + "Noelle/", "Noelle_idle_", 0, 9),
            move = Load(Chars + "Noelle/", "Noelle_run_", 0, 7),
            jump = Load(Chars + "Noelle/", "Noelle_jump_", 0, 2),
            special = Load(Chars + "Noelle/Cast/", "Noelle_cast_", 20, 27),      // both arms swing up and out: her dance move
        };

        if (isNew)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(art, AssetPath);
        }
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssets();

        if (art.Complete) { if (report) Debug.Log("Celebration art is ready (" + AssetPath + ")."); }
        else Debug.LogWarning("The celebration art is incomplete: a picture of Kris, Susie, Ralsei or Noelle was not found (Assets/Characters/Dance).");
    }

    static Sprite[] Load(string folder, string prefix, int from, int to)
    {
        var list = new List<Sprite>();
        for (int i = from; i <= to; i++)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{folder}{prefix}{i}.png");
            if (sprite != null) list.Add(sprite);
        }
        return list.ToArray();
    }
}
