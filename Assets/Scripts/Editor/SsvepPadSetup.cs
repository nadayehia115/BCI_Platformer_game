using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the SSVEP pad prefab: Assets/Resources/SsvepPad.prefab, a round pad with a crosshair in the middle (SsvepPad component).
// The two pictures it uses (a white disc and a white crosshair, tinted black / white by the flicker) are drawn here and saved in
// Assets/SSVEP. It runs by itself when the prefab is missing, so nobody has to remember it; Tools > Setup SSVEP Pad makes it again
// (this overwrites the prefab, so run it only if you want the default one back).
public static class SsvepPadSetup
{
    const string Folder = "Assets/SSVEP/";
    const string PrefabPath = "Assets/Resources/SsvepPad.prefab";
    const int Size = 256;
    const float CrosshairSize = 110f;              // of the pad's 200 units: the arms are about a quarter of its width

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Build(false);
        };
    }

    [MenuItem("Tools/Setup SSVEP Pad")]
    static void Menu() => Build(true);

    static void Build(bool report)
    {
        Directory.CreateDirectory(Folder);
        var circle = MakeSprite("ssvep_circle.png", (x, y) => Circle(x, y));
        var cross = MakeSprite("ssvep_crosshair.png", (x, y) => Crosshair(x, y));
        if (circle == null || cross == null)
        {
            Debug.LogWarning("The SSVEP pad pictures could not be made.");
            return;
        }

        var root = new GameObject("SsvepPad", typeof(RectTransform));
        ((RectTransform)root.transform).sizeDelta = new Vector2(200f, 200f);

        // a thin grey ring so a white pad can still be found on a light background (it does not flicker)
        var ring = NewImage("Ring", root.transform, circle, 212f, new Color(0.35f, 0.35f, 0.35f));
        var pad = NewImage("Pad", root.transform, circle, 200f, Color.white);
        var crosshair = NewImage("Crosshair", root.transform, cross, CrosshairSize, Color.black);
        ring.transform.SetAsFirstSibling();

        var component = root.AddComponent<SsvepPad>();
        var so = new SerializedObject(component);
        so.FindProperty("pad").objectReferenceValue = pad;
        so.FindProperty("crosshair").objectReferenceValue = crosshair;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        if (report) Debug.Log("SSVEP pad prefab made: " + PrefabPath);
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, float size, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    // ---- the pictures ----

    // A white disc with a one pixel soft edge.
    static float Circle(int x, int y)
    {
        float dx = x + 0.5f - Size / 2f, dy = y + 0.5f - Size / 2f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(Size / 2f - r);
    }

    // A white plus sign: two bars 14 px thick and 128 px long in the middle of the picture (drawn 110 / 256 of the 200 units of the pad).
    static float Crosshair(int x, int y)
    {
        int cx = Size / 2, cy = Size / 2;
        bool horizontal = Mathf.Abs(y + 0.5f - cy) <= 7f && Mathf.Abs(x + 0.5f - cx) <= 64f;
        bool vertical = Mathf.Abs(x + 0.5f - cx) <= 7f && Mathf.Abs(y + 0.5f - cy) <= 64f;
        return horizontal || vertical ? 1f : 0f;
    }

    static Sprite MakeSprite(string file, System.Func<int, int, float> alpha)
    {
        string path = Folder + file;
        if (!File.Exists(path))
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(x, y)));
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        AssetDatabase.ImportAsset(path);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = 100;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
