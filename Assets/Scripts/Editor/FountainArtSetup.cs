using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Builds Assets/Resources/FountainArt.asset (the list of sprites the dark fountain sequence uses) from the pictures in
// Assets/deltarune. It runs by itself after Unity compiles / opens the project whenever the asset is missing or incomplete, so
// nobody has to remember to run it; Tools > Setup Fountain Art does it on demand.
public static class FountainArtSetup
{
    const string AssetPath = "Assets/Resources/FountainArt.asset";
    const string Folder = "Assets/deltarune/";

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var art = AssetDatabase.LoadAssetAtPath<FountainArt>(AssetPath);
            if (art == null || !art.Complete) Build(false);
        };
    }

    [MenuItem("Tools/Setup Fountain Art")]
    static void Menu() => Build(true);

    static void Build(bool report)
    {
        var art = AssetDatabase.LoadAssetAtPath<FountainArt>(AssetPath);
        bool isNew = art == null;
        if (isNew) art = ScriptableObject.CreateInstance<FountainArt>();

        art.making = Slices("Makingfountain");
        art.krisTransform = Slices("Kris_transform");
        art.sparkles = Slices("Fountain_opening_eff");

        var all = art.making.Concat(art.krisTransform).Concat(art.sparkles).Where(sp => sp != null).ToArray();
        art.padSprites = all;
        art.pads = all.Select(Pad).ToArray();

        if (isNew)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(art, AssetPath);
        }
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssets();

        if (report || !art.Complete)
        {
            if (art.Complete) Debug.Log("Fountain art is ready (" + AssetPath + ").");
            else Debug.LogWarning("The fountain art is incomplete: check that Assets/deltarune has Makingfountain, Kris_transform and Fountain_opening_eff, sliced into sprites.");
        }
    }

    // How much empty space (world units) a picture has under its lowest visible pixel: counted from the PNG file itself, because
    // textures cannot be read in a built game.
    static readonly Dictionary<string, Color32[]> pixelCache = new Dictionary<string, Color32[]>();
    static readonly Dictionary<string, int> widthCache = new Dictionary<string, int>();

    static float Pad(Sprite sprite)
    {
        string path = AssetDatabase.GetAssetPath(sprite.texture);
        if (!pixelCache.TryGetValue(path, out var pixels))
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            pixels = tex.GetPixels32();
            pixelCache[path] = pixels;
            widthCache[path] = tex.width;
            Object.DestroyImmediate(tex);
        }
        int width = widthCache[path];
        Rect r = sprite.rect;
        int empty = 0;
        for (int row = 0; row < (int)r.height; row++)                  // from the bottom row of the picture upwards
        {
            bool any = false;
            for (int x = 0; x < (int)r.width && !any; x++)
                any = pixels[((int)r.y + row) * width + (int)r.x + x].a > 0;
            if (any) break;
            empty++;
        }
        return empty / sprite.pixelsPerUnit;
    }

    // All the slices of a sheet, in order (Name_0, Name_1, ...).
    static Sprite[] Slices(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath($"{Folder}{name}.png").OfType<Sprite>()
            .OrderBy(s => int.TryParse(s.name.Substring(s.name.LastIndexOf('_') + 1), out int n) ? n : 0).ToArray();
    }
}
