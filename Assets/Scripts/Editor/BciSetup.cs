using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Sets up everything the BCI flow needs in the project, by itself when Unity opens (and with Tools > Setup BCI Flow):
//   * Assets/Resources/BciSettings.asset   the Inspector settings (PythonInterpreterPath ...)
//   * Assets/Resources/BciArt.asset        tiles of the game's tileset for the loading screen's mini-game
//   * the five stage scenes (BciContrast, BciHeadsetCheck, TrainingCalibration, SessionCalibration, BciLoading), each with one object
//     that builds the screen in code, added to the Build Settings after MainMenu and GameScene.
public static class BciSetup
{
    const string SettingsPath = "Assets/Resources/BciSettings.asset";
    const string ArtPath = "Assets/Resources/BciArt.asset";
    const string TilesetPath = "Assets/Tiles/tileArt/tilemap_packed.png";

    static readonly (string scene, System.Type stage)[] Scenes =
    {
        (BciFlow.ContrastScene, typeof(BciContrastStage)),
        (BciFlow.HeadsetScene, typeof(BciHeadsetStage)),
        (BciFlow.TrainingScene, typeof(BciCalibrationStage)),
        (BciFlow.SessionScene, typeof(BciCalibrationStage)),
        (BciFlow.LoadingScene, typeof(BciLoadingStage)),
    };

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (NeedsSetup()) Run(false);
        };
    }

    [MenuItem("Tools/Setup BCI Flow")]
    static void Menu() => Run(true);

    static bool NeedsSetup()
    {
        if (AssetDatabase.LoadAssetAtPath<BciSettings>(SettingsPath) == null) return true;
        var art = AssetDatabase.LoadAssetAtPath<BciArt>(ArtPath);
        if (art == null || art.crate == null || art.tennaTalk == null || art.tennaPoint == null || art.tennaLaugh == null) return true;
        foreach (var (scene, _) in Scenes)
            if (!File.Exists($"Assets/Scenes/{scene}.unity")) return true;
        var inBuild = EditorBuildSettings.scenes.Select(s => s.path).ToHashSet();
        return Scenes.Any(s => !inBuild.Contains($"Assets/Scenes/{s.scene}.unity"));
    }

    static void Run(bool report)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        if (AssetDatabase.LoadAssetAtPath<BciSettings>(SettingsPath) == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BciSettings>(), SettingsPath);

        var art = AssetDatabase.LoadAssetAtPath<BciArt>(ArtPath);
        bool newArt = art == null;
        if (newArt) art = ScriptableObject.CreateInstance<BciArt>();
        var tiles = AssetDatabase.LoadAllAssetsAtPath(TilesetPath).OfType<Sprite>().ToDictionary(s => s.name, s => s);
        tiles.TryGetValue("tilemap_packed_2", out art.grass);
        tiles.TryGetValue("tilemap_packed_5", out art.dirt);
        tiles.TryGetValue("tilemap_packed_26", out art.crate);
        art.tennaTalk = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/deltarune/Tenna_Pointup_pose.png");
        art.tennaPoint = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/deltarune/Tenna_point_at_screen.png");
        art.tennaLaugh = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/deltarune/Tenna_laughing.png");
        if (newArt) AssetDatabase.CreateAsset(art, ArtPath);
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssets();

        foreach (var (scene, stage) in Scenes) EnsureScene(scene, stage);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        if (report) Debug.Log("BCI flow is set up (settings, art, 5 scenes in the Build Settings).");
    }

    static void EnsureScene(string name, System.Type stage)
    {
        string path = $"Assets/Scenes/{name}.unity";
        if (File.Exists(path)) return;

        // Additive: the scene the user has open is not touched.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        new GameObject("BCI Stage (" + name + ")").AddComponent(stage);
        EditorSceneManager.SaveScene(scene, path);
        EditorSceneManager.CloseScene(scene, true);
        Debug.Log("BCI: scene created: " + path);
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        foreach (var (scene, _) in Scenes)
        {
            string path = $"Assets/Scenes/{scene}.unity";
            if (!File.Exists(path) || list.Any(s => s.path == path)) continue;
            list.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
    }
}

// The BCI pictures in Assets/Resources/Bci (the brain, the electrode gem, an optional character.png) are pixel art Sprites that keep their size.
public class BciSpritesImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Bci/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
