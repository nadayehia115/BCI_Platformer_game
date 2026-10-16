using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Setup Noelle Ice
// Turns Noelle (under Companions) from a follower into the one who freezes water: removes her CompanionFollow and adds
// NoelleFreeze, connected to the HUD switcher. She finds the pools in Tilemap_Water by herself while the game runs; the ice
// goes into Tilemap_Ice, which gets its collider when the first pool freezes. Run it after Tools > Add Companions To Scene.
public static class NoelleIceSetup
{
    [MenuItem("Tools/Setup Noelle Ice")]
    internal static void Setup()
    {
        CompanionFollow follow = null;
        foreach (var f in Object.FindObjectsByType<CompanionFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (f.name == "Noelle") follow = f;
        GameObject noelle = follow != null ? follow.gameObject : null;
        if (noelle == null)
            foreach (var n in Object.FindObjectsByType<NoelleFreeze>(FindObjectsInactive.Include, FindObjectsSortMode.None)) noelle = n.gameObject;
        if (noelle == null)
        {
            EditorUtility.DisplayDialog("Setup Noelle Ice", "No Noelle found. Run Tools > Add Companions To Scene first.", "OK");
            return;
        }

        bool hasWater = false, hasIce = false;
        foreach (var tm in Object.FindObjectsByType<UnityEngine.Tilemaps.Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.name == "Tilemap_Water") hasWater = true;
            if (tm.name == "Tilemap_Ice") hasIce = true;
        }
        if (!hasWater || !hasIce) Debug.LogWarning("The scene needs tilemaps called Tilemap_Water and Tilemap_Ice for Noelle's power.");

        if (follow != null) Undo.DestroyObjectImmediate(follow);

        var freeze = noelle.GetComponent<NoelleFreeze>();
        if (!freeze) freeze = Undo.AddComponent<NoelleFreeze>(noelle);
        var so = new SerializedObject(freeze);
        so.FindProperty("switcher").objectReferenceValue = Object.FindFirstObjectByType<CharacterSwitcher>();
        var frames = ImportCastFrames();
        var castFrames = so.FindProperty("castFrames");
        castFrames.arraySize = frames.Length;
        for (int i = 0; i < frames.Length; i++) castFrames.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // the orb, flash, puddle, snowflake and snow (tools/extract_noelle_effects.py writes the pictures)
        var fx = noelle.GetComponent<NoelleEffects>();
        if (!fx) fx = Undo.AddComponent<NoelleEffects>(noelle);
        var fxSo = new SerializedObject(fx);
        fxSo.FindProperty("orb").objectReferenceValue = ImportEffect("fx_orb");
        fxSo.FindProperty("flash").objectReferenceValue = ImportEffect("fx_disc");
        fxSo.FindProperty("patch").objectReferenceValue = ImportEffect("fx_patch");
        fxSo.FindProperty("snowflake").objectReferenceValue = ImportEffect("fx_snowflake");
        fxSo.FindProperty("snowA").objectReferenceValue = ImportEffect("fx_snow_a");
        fxSo.FindProperty("snowB").objectReferenceValue = ImportEffect("fx_snow_b");
        fxSo.ApplyModifiedPropertiesWithoutUndo();

        // drawn with Kris, not behind the ground
        var kris = Object.FindFirstObjectByType<PlayerController>();
        var krisRenderer = kris != null ? kris.GetComponent<SpriteRenderer>() : null;
        var sr = noelle.GetComponent<SpriteRenderer>();
        if (krisRenderer && sr)
        {
            sr.sortingLayerID = krisRenderer.sortingLayerID;
            sr.sortingOrder = krisRenderer.sortingOrder;
        }

        EditorSceneManager.MarkSceneDirty(noelle.scene);
        Selection.activeGameObject = noelle;
        Debug.Log($"Noelle is ready ({frames.Length} spell frames): she freezes the water pool nearest to Kris, one pool per press. Save the scene (Ctrl+S).");
    }

    static Sprite ImportEffect(string name)
    {
        string path = $"Assets/Characters/Noelle/Effects/{name}.png";
        if (!File.Exists(path))
        {
            Debug.LogWarning("Missing " + path + ". Run python tools/extract_noelle_effects.py first.");
            return null;
        }
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.spritePixelsPerUnit = 16;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        imp.SetTextureSettings(settings);
        imp.spritePivot = new Vector2(0.5f, 0.5f);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Her spell frames (tools/extract_noelle_cast.py writes them), imported like her other frames: 16 pixels per unit, point
    // filtering, centre pivot, one common canvas with the feet on the bottom row.
    static Sprite[] ImportCastFrames()
    {
        const string folder = "Assets/Characters/Noelle/Cast";
        AssetDatabase.Refresh();
        if (!Directory.Exists(folder))
        {
            Debug.LogWarning("No " + folder + ". Run python tools/extract_noelle_cast.py first; Noelle keeps her idle animation until then.");
            return new Sprite[0];
        }
        var files = Directory.GetFiles(folder, "Noelle_cast_*.png")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_').Last()))
            .ToArray();
        foreach (var path in files)
        {
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.filterMode = FilterMode.Point;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.spritePixelsPerUnit = 16;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            imp.SetTextureSettings(settings);
            imp.spritePivot = new Vector2(0.5f, 0.5f);
            imp.SaveAndReimport();
        }
        return files.Select(f => AssetDatabase.LoadAssetAtPath<Sprite>(f)).ToArray();
    }
}
