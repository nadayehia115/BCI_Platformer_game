using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Tools > Build Main Menu Scene : generates Assets/Scenes/MainMenu.unity from the art in Assets/Menu
// (see tools/generate_menu_art.py and tools/generate_menu_audio.py) and the tile sheet.
public static class MainMenuBuilder
{
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Scenes/GameScene.unity";
    const string SpriteDir = "Assets/Menu/Sprites/";
    const string AudioDir = "Assets/Menu/Audio/";
    const string FontFile = "Assets/Menu/Fonts/LilitaOne-Regular.ttf";
    const string FontAssetPath = "Assets/Menu/Fonts/LilitaOne SDF.asset";
    const string CharacterDir = "Assets/Menu/Characters";
    const string SheetPath = "Assets/tiles/tileArt/tilemap_packed.png";

    const float Tile = 72f;                                   // 18px tile drawn at 4x
    static readonly Vector2 ArtSize = new Vector2(27 * Tile, 15 * Tile);
    static TMP_FontAsset uiFont;                                       // Lilita One, set when the scene is built
    static readonly Color Ink = Color.black;                            // all text is pure black for readability
    static readonly Color Sky = new Color32(225, 246, 244, 255);

    [MenuItem("Tools/Build Main Menu Scene")]
    static void BuildInteractive() => Build(true);

    // For command-line use: Unity -batchmode -executeMethod MainMenuBuilder.BuildBatch
    public static void BuildBatch() => Build(false);

    static void Build(bool interactive)
    {
        if (!EnsureTextMeshPro(interactive)) return;
        if (interactive)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Build Main Menu", "MainMenu.unity already exists. Rebuild it from scratch?", "Rebuild", "Cancel"))
                return;
        }

        AssetDatabase.Refresh();
        uiFont = EnsureFont();
        ImportSprite("menu_background.png", Vector4.zero);
        ImportSprite("button.png", new Vector4(8, 8, 8, 8));
        ImportSprite("panel.png", new Vector4(8, 8, 8, 8));
        ImportSprite("slider_track.png", new Vector4(4, 4, 4, 4));
        ImportSprite("slider_fill.png", new Vector4(4, 4, 4, 4));
        ImportSprite("speaker_on.png", Vector4.zero);
        ImportSprite("speaker_off.png", Vector4.zero);
        ImportSprite("window.png", new Vector4(8, 8, 8, 8));
        foreach (var f in Directory.GetFiles(CharacterDir, "*.png"))
            ImportAt(f.Replace('\\', '/'), Vector4.zero);

        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        Sprite Sheet(int i) => sprites["tilemap_packed_" + i];
        Sprite Load(string n) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + n);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- camera ------------------------------------------------------------------------
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0, 0, -10);
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Sky;

        // ---- canvas + event system -------------------------------------------------------
        var canvasGo = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        esGo.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        // ---- audio -----------------------------------------------------------------------
        var audioGo = new GameObject("AudioManager", typeof(AudioManager));
        var music = NewAudioSource(audioGo.transform, "Music", true);
        var sfx = NewAudioSource(audioGo.transform, "SFX", false);
        var audioSo = new SerializedObject(audioGo.GetComponent<AudioManager>());
        audioSo.FindProperty("musicSource").objectReferenceValue = music;
        audioSo.FindProperty("sfxSource").objectReferenceValue = sfx;
        audioSo.FindProperty("menuMusic").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "menu_music.wav");
        audioSo.FindProperty("hoverClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "ui_hover.wav");
        audioSo.FindProperty("clickClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "ui_click.wav");
        audioSo.FindProperty("backClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "ui_back.wav");
        audioSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- animated background -----------------------------------------------------------
        var art = NewRect("ArtRoot", canvasGo.transform);
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0.5f);
        art.sizeDelta = ArtSize;
        art.gameObject.AddComponent<ArtRootFit>();

        var bg = NewImage("Background", art, Load("menu_background.png"));
        Stretch(bg.rectTransform);

        AddCloud(art, "Cloud1", Sheet, 1.0f, 11.6f, 14f, 0.0f);
        AddCloud(art, "Cloud2", Sheet, 7.5f, 9.6f, 9f, 0.0f);
        AddCloud(art, "Cloud3", Sheet, 17.5f, 12.6f, 19f, 0.0f);

        AddDecor(art, "Coin1", Sheet(151), 6.4f, 7.6f, 10f, 1.8f, 0.00f);
        AddDecor(art, "Coin2", Sheet(151), 7.6f, 8.2f, 10f, 1.8f, 0.20f);
        AddDecor(art, "Coin3", Sheet(151), 8.8f, 7.6f, 10f, 1.8f, 0.40f);
        AddDecor(art, "Key", Sheet(27), 18.6f, 6.4f, 12f, 2.4f, 0.10f);
        AddDecor(art, "Gem", Sheet(67), 19.4f, 4.6f, 10f, 2.0f, 0.55f);

        // ---- UI layer ----------------------------------------------------------------------
        var ui = NewRect("UI", canvasGo.transform);
        Stretch(ui);
        ui.gameObject.AddComponent<UIFit>();

        var buttonSprite = Load("button.png");
        var panelSprite = Load("panel.png");

        // main panel
        var mainPanel = NewRect("MainPanel", ui);
        Stretch(mainPanel);
        var mainGroup = mainPanel.gameObject.AddComponent<CanvasGroup>();

        var title = NewText("Title", mainPanel, "BCI BUSTERS", 140, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.anchoredPosition = new Vector2(0, -120);
        title.rectTransform.sizeDelta = new Vector2(1500, 200);
        var titleDecor = title.gameObject.AddComponent<MenuDecor>();
        titleDecor.Configure(MenuDecor.Mode.Bob, 8f, 3.2f, 0f, 0f);

        var stack = NewRect("Buttons", mainPanel);
        stack.anchorMin = stack.anchorMax = stack.pivot = new Vector2(0.5f, 0.5f);
        stack.anchoredPosition = new Vector2(0, -90);
        stack.sizeDelta = new Vector2(700, 470);
        var vlg = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 30;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = vlg.childControlHeight = false;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

        var playBtn = NewButton(stack, "PlayButton", "PLAY", buttonSprite, new Vector2(640, 130), 64, false);
        var howBtn = NewButton(stack, "HowToPlayButton", "HOW TO PLAY", buttonSprite, new Vector2(640, 130), 64, false);
        var quitBtn = NewButton(stack, "QuitButton", "QUIT GAME", buttonSprite, new Vector2(640, 130), 64, false);

        // how to play, page 1: the team (2 x 2 cards on a frame)
        var page1 = NewRect("HowToPlayPage1", ui);
        Stretch(page1);
        var page1Group = page1.gameObject.AddComponent<CanvasGroup>();
        var frame1 = NewFrame(page1, panelSprite);

        var cards = NewRect("Cards", frame1);
        cards.anchorMin = cards.anchorMax = new Vector2(0.5f, 1f);
        cards.pivot = new Vector2(0.5f, 1f);
        cards.anchoredPosition = new Vector2(0, -40);
        cards.sizeDelta = new Vector2(1500, 604);
        var grid = cards.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(735, 290);
        grid.spacing = new Vector2(30, 24);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.MiddleCenter;

        var windowSprite = Load("window.png");
        NewCard(cards, "Kris", "Team Leader", "Runs & jumps with W/A/D", panelSprite, buttonSprite, windowSprite);
        NewCard(cards, "Noelle", "Frost Magic", "Turns water into ice", panelSprite, buttonSprite, windowSprite);
        NewCard(cards, "Ralsei", "Dark Prince", "Hangs to form platforms", panelSprite, buttonSprite, windowSprite);
        NewCard(cards, "Susie", "The Muscle", "Pushes heavy barriers", panelSprite, buttonSprite, windowSprite);

        var bar1 = NewNavBar(frame1);
        var exit1 = NewButton(bar1, "ExitButton", "EXIT", buttonSprite, new Vector2(300, 84), 48, true);
        var nextBtn = NewButton(bar1, "NextButton", "NEXT", buttonSprite, new Vector2(300, 84), 48, false);
        NewHeading(page1, "HOW TO PLAY");       // created last so it draws on top

        // how to play, page 2: the briefing
        var page2 = NewRect("HowToPlayPage2", ui);
        Stretch(page2);
        var page2Group = page2.gameObject.AddComponent<CanvasGroup>();
        var frame2 = NewFrame(page2, panelSprite);

        var briefing = NewText("Briefing", frame2,
            "<b>Hi!</b>\n" +
            "We are the <b>BCI Busters</b>, and we need your help to clear the path!\n" +
            "Reach the flag before the clock runs out:\n" +
            "\u2022  Move Kris with A / D and jump with W.\n" +
            "\u2022  Stop at an obstacle and press S: it awakens the power of Kris's sword, the dark fountain opens and a flashing pad appears.\n" +
            "\u2022  Focus on the pad: the power of your focus calls upon <b>Noelle</b> for water, <b>Ralsei</b> for trees or <b>Susie</b> for boxes, to help you overcome the obstacle you are standing at.\n" +
            "<b>The faster you finish, the higher your score!</b>",
            48, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        briefing.textWrappingMode = TextWrappingModes.Normal;
        briefing.enableAutoSizing = true;
        briefing.fontSizeMin = 34;
        briefing.fontSizeMax = 50;
        briefing.paragraphSpacing = 18;
        Stretch(briefing.rectTransform);
        briefing.rectTransform.offsetMin = new Vector2(70, 140);
        briefing.rectTransform.offsetMax = new Vector2(-70, -40);

        var bar2 = NewNavBar(frame2);
        var prevBtn = NewButton(bar2, "PreviousButton", "PREVIOUS", buttonSprite, new Vector2(300, 84), 48, true);
        var exit2 = NewButton(bar2, "ExitButton", "EXIT", buttonSprite, new Vector2(300, 84), 48, true);
        NewHeading(page2, "HOW TO PLAY");

        // audio controls (always visible, top right)
        var audioPanel = NewRect("AudioControls", ui);
        audioPanel.SetSiblingIndex(1);                      // above the main menu, but below the How to Play pages: their heading draws in front of it
        audioPanel.anchorMin = audioPanel.anchorMax = audioPanel.pivot = new Vector2(1, 1);
        audioPanel.anchoredPosition = new Vector2(-40, -30);
        audioPanel.sizeDelta = new Vector2(580, 100);
        var hlg = audioPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        var musicBox = NewSliderBox(audioPanel, "MusicBox", "MUSIC", Load("slider_track.png"), Load("slider_fill.png"), Sheet(151), out var musicSlider);
        var sfxBox = NewSliderBox(audioPanel, "SfxBox", "EFFECTS", Load("slider_track.png"), Load("slider_fill.png"), Sheet(151), out var sfxSlider);

        var muteBtn = NewButton(audioPanel, "MuteButton", "", buttonSprite, new Vector2(84, 84), 10, false);
        var icon = NewImage("Icon", muteBtn.transform, Load("speaker_on.png"));
        icon.preserveAspect = true;
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        icon.rectTransform.sizeDelta = new Vector2(52, 52);
        muteBtn.transform.SetAsFirstSibling();

        // ---- controller + wiring ------------------------------------------------------------
        var controller = new GameObject("MenuController", typeof(MenuNavigation), typeof(SettingsMenu));
        var nav = controller.GetComponent<MenuNavigation>();
        var navSo = new SerializedObject(nav);
        navSo.FindProperty("mainPanel").objectReferenceValue = mainGroup;
        navSo.FindProperty("howToPage1").objectReferenceValue = page1Group;
        navSo.FindProperty("howToPage2").objectReferenceValue = page2Group;
        navSo.FindProperty("gameSceneName").stringValue = "GameScene";
        navSo.FindProperty("firstMainButton").objectReferenceValue = playBtn.gameObject;
        navSo.FindProperty("page1FirstButton").objectReferenceValue = nextBtn.gameObject;
        navSo.FindProperty("page2FirstButton").objectReferenceValue = prevBtn.gameObject;
        navSo.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(playBtn.onClick, nav.Play);
        UnityEventTools.AddPersistentListener(howBtn.onClick, nav.ShowHowToPlay);
        UnityEventTools.AddPersistentListener(quitBtn.onClick, nav.Quit);
        UnityEventTools.AddPersistentListener(nextBtn.onClick, nav.NextPage);
        UnityEventTools.AddPersistentListener(prevBtn.onClick, nav.PreviousPage);
        UnityEventTools.AddPersistentListener(exit1.onClick, nav.ExitToMenu);
        UnityEventTools.AddPersistentListener(exit2.onClick, nav.ExitToMenu);

        var setSo = new SerializedObject(controller.GetComponent<SettingsMenu>());
        setSo.FindProperty("musicSlider").objectReferenceValue = musicSlider;
        setSo.FindProperty("sfxSlider").objectReferenceValue = sfxSlider;
        setSo.FindProperty("muteButton").objectReferenceValue = muteBtn;
        setSo.FindProperty("speakerIcon").objectReferenceValue = icon;
        setSo.FindProperty("speakerOn").objectReferenceValue = Load("speaker_on.png");
        setSo.FindProperty("speakerOff").objectReferenceValue = Load("speaker_off.png");
        setSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- save + build settings -----------------------------------------------------------
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        Selection.activeGameObject = canvasGo;
        Debug.Log("Main menu built: " + ScenePath + ". Press Play to try it. (MainMenu is scene 0 in Build Settings.)");
    }

    // ---- environment checks ----------------------------------------------------------------
    static bool EnsureTextMeshPro(bool interactive)
    {
        const string settings = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        if (File.Exists(settings)) return true;
        const string pkg = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        if (File.Exists(pkg))
        {
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
        }
        if (File.Exists(settings)) return true;
        if (interactive)
            EditorUtility.DisplayDialog("TextMesh Pro",
                "TextMesh Pro essentials are being imported. Wait for the import to finish, then run Tools > Build Main Menu Scene again.", "OK");
        else
            Debug.LogError("TextMesh Pro essentials missing; imported them now, run again.");
        return false;
    }

    // Builds (once) a TextMesh Pro font asset from the Lilita One .ttf using TMP's own "Create Font Asset" command.
    // Anything the font lacks falls back to the default font.
    static TMP_FontAsset EnsureFont()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null && FontAssetIsHealthy(existing)) return existing;
        if (File.Exists(FontAssetPath)) AssetDatabase.DeleteAsset(FontAssetPath);     // a half-built leftover

        var font = AssetDatabase.LoadAssetAtPath<Font>(FontFile);
        if (font == null)
        {
            Debug.LogWarning("Font not found at " + FontFile + "; using the default font.");
            return null;
        }

        const string generated = "Assets/Menu/Fonts/LilitaOne-Regular SDF.asset";    // name TMP gives it
        if (File.Exists(generated)) AssetDatabase.DeleteAsset(generated);
        Selection.activeObject = font;
        EditorApplication.ExecuteMenuItem("Assets/Create/TextMeshPro/Font Asset/SDF");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(generated);
        if (fa == null)
        {
            Debug.LogWarning("Could not create the Lilita One font asset; using the default font.");
            return null;
        }
        string err = AssetDatabase.MoveAsset(generated, FontAssetPath);
        if (!string.IsNullOrEmpty(err)) Debug.LogWarning(err);
        fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(File.Exists(FontAssetPath) ? FontAssetPath : generated);

        // pre-generate the characters the menu uses so the atlas is saved with the asset
        fa.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~\u2022");
        if (TMP_Settings.defaultFontAsset != null && !fa.fallbackFontAssetTable.Contains(TMP_Settings.defaultFontAsset))
            fa.fallbackFontAssetTable.Add(TMP_Settings.defaultFontAsset);

        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    static bool FontAssetIsHealthy(TMP_FontAsset fa)
    {
        try { return fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null && fa.material != null; }
        catch { return false; }
    }

    static void ImportSprite(string file, Vector4 border) => ImportAt(SpriteDir + file, border);

    static void ImportAt(string path, Vector4 border)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Debug.LogError("Missing sprite " + path); return; }
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.spritePixelsPerUnit = 100;
        imp.spriteBorder = border;
        imp.SaveAndReimport();
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        if (File.Exists(GameScenePath) && list.All(s => s.path != GameScenePath))
            list.Add(new EditorBuildSettingsScene(GameScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ---- UI helpers ------------------------------------------------------------------------
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) t.font = uiFont;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style & ~FontStyles.Bold;
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static Button NewButton(Transform parent, string name, string label, Sprite sprite, Vector2 size, float fontSize, bool isBack)
    {
        var rt = NewRect(name, parent);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 0.25f;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1f, 0.97f, 0.86f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
        btn.colors = colors;

        var anim = rt.gameObject.AddComponent<UIButtonAnimator>();
        var so = new SerializedObject(anim);
        so.FindProperty("isBackButton").boolValue = isBack;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (!string.IsNullOrEmpty(label))
        {
            var text = NewText("Label", rt, label, fontSize, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(0, 14);   // nudge up to sit on the button face, above the bottom shade
        }
        return btn;
    }

    static RectTransform NewSliderBox(Transform parent, string name, string label, Sprite track, Sprite fill, Sprite handle, out Slider slider)
    {
        var box = NewRect(name, parent);
        box.sizeDelta = new Vector2(220, 100);

        var text = NewText("Label", box, label, 30, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
        text.rectTransform.anchorMin = new Vector2(0, 1);
        text.rectTransform.anchorMax = new Vector2(1, 1);
        text.rectTransform.pivot = new Vector2(0.5f, 1);
        text.rectTransform.anchoredPosition = Vector2.zero;
        text.rectTransform.sizeDelta = new Vector2(0, 38);

        var root = NewRect("Slider", box);
        root.anchorMin = new Vector2(0, 0);
        root.anchorMax = new Vector2(1, 0);
        root.pivot = new Vector2(0.5f, 0);
        root.anchoredPosition = new Vector2(0, 6);
        root.sizeDelta = new Vector2(0, 50);

        var bg = NewImage("Background", root, track);
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 0.5f;
        bg.rectTransform.anchorMin = new Vector2(0, 0.5f);
        bg.rectTransform.anchorMax = new Vector2(1, 0.5f);
        bg.rectTransform.sizeDelta = new Vector2(0, 26);

        var fillArea = NewRect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0, 0.5f);
        fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.sizeDelta = new Vector2(0, 26);
        var fillImg = NewImage("Fill", fillArea, fill);
        fillImg.type = Image.Type.Sliced;
        fillImg.pixelsPerUnitMultiplier = 0.5f;
        fillImg.rectTransform.anchorMin = Vector2.zero;
        fillImg.rectTransform.anchorMax = Vector2.one;
        fillImg.rectTransform.offsetMin = fillImg.rectTransform.offsetMax = Vector2.zero;

        var slideArea = NewRect("Handle Slide Area", root);
        Stretch(slideArea);
        slideArea.offsetMin = new Vector2(24, 0);
        slideArea.offsetMax = new Vector2(-24, 0);
        var handleImg = NewImage("Handle", slideArea, handle);
        handleImg.raycastTarget = true;
        handleImg.rectTransform.sizeDelta = new Vector2(48, 48);

        slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fillImg.rectTransform;
        slider.handleRect = handleImg.rectTransform;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.6f;
        return box;
    }

    static AudioSource NewAudioSource(Transform parent, string name, bool loop)
    {
        var go = new GameObject(name, typeof(AudioSource));
        go.transform.SetParent(parent, false);
        var src = go.GetComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        return src;
    }


    // ---- how to play helpers ---------------------------------------------------------------
    static void NewHeading(Transform parent, string text)
    {
        var t = NewText("Heading", parent, text, 96, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        t.rectTransform.pivot = new Vector2(0.5f, 1f);
        t.rectTransform.anchoredPosition = new Vector2(0, -132);       // below the music controls (they end 130 from the top)
        t.rectTransform.sizeDelta = new Vector2(1400, 120);
    }

    // The big parchment frame shared by both How to Play pages.
    static RectTransform NewFrame(Transform parent, Sprite panelSprite)
    {
        var frame = NewImage("Frame", parent, panelSprite);
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = 0.25f;
        frame.raycastTarget = true;
        var r = frame.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0, -100);                     // lower than it was: the heading above it now sits under the music controls
        r.sizeDelta = new Vector2(1600, 800);
        return r;
    }

    static RectTransform NewNavBar(Transform parent)
    {
        var bar = NewRect("NavButtons", parent);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1, 0);
        bar.anchoredPosition = new Vector2(-40, 34);
        bar.sizeDelta = new Vector2(660, 90);
        var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 24;
        h.childAlignment = TextAnchor.MiddleRight;
        h.childControlWidth = h.childControlHeight = false;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        return bar;
    }

    static Sprite[] LoadFrames(string character)
    {
        return Directory.GetFiles(CharacterDir, character + "_*.png")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_').Last()))
            .Select(f => AssetDatabase.LoadAssetAtPath<Sprite>(f))
            .ToArray();
    }

    // A parchment card: animated character in a sky-blue window on the left, name ribbon + role + description on the right.
    static void NewCard(Transform parent, string character, string role, string description, Sprite cardSprite, Sprite ribbonSprite, Sprite windowSprite)
    {
        var card = NewImage(character + "Card", parent, cardSprite);
        card.type = Image.Type.Sliced;
        card.pixelsPerUnitMultiplier = 0.25f;
        card.color = new Color(0.97f, 0.90f, 0.78f);      // warmer tan so the cards stand out on the cream frame

        var window = NewImage("Window", card.transform, windowSprite);
        window.type = Image.Type.Sliced;
        window.pixelsPerUnitMultiplier = 0.25f;
        var wr = window.rectTransform;
        wr.anchorMin = wr.anchorMax = new Vector2(0, 0.5f);
        wr.pivot = new Vector2(0, 0.5f);
        wr.anchoredPosition = new Vector2(26, 0);
        wr.sizeDelta = new Vector2(250, 260);

        var frames = LoadFrames(character);
        var art = NewImage("Character", window.transform, frames.Length > 0 ? frames[0] : null);
        var ar = art.rectTransform;
        ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(0.5f, 0.5f);
        ar.anchoredPosition = new Vector2(0, -4);
        if (frames.Length > 0)
        {
            // integer pixel scale keeps the pixel art crisp
            float fit = Mathf.Min(220f / frames[0].rect.width, 232f / frames[0].rect.height);
            float scale = Mathf.Max(1f, Mathf.Floor(fit));
            ar.sizeDelta = new Vector2(frames[0].rect.width * scale, frames[0].rect.height * scale);
        }
        var flip = art.gameObject.AddComponent<SpriteFlipbook>();
        var so = new SerializedObject(flip);
        var prop = so.FindProperty("frames");
        prop.arraySize = frames.Length;
        for (int i = 0; i < frames.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
        so.FindProperty("fps").floatValue = 8f;
        so.ApplyModifiedPropertiesWithoutUndo();

        var info = NewRect("Info", card.transform);
        info.anchorMin = Vector2.zero;
        info.anchorMax = Vector2.one;
        info.offsetMin = new Vector2(300, 24);
        info.offsetMax = new Vector2(-26, -24);

        var ribbon = NewImage("Ribbon", info, ribbonSprite);
        ribbon.type = Image.Type.Sliced;
        ribbon.pixelsPerUnitMultiplier = 0.25f;
        var rr = ribbon.rectTransform;
        rr.anchorMin = new Vector2(0, 1);
        rr.anchorMax = new Vector2(1, 1);
        rr.pivot = new Vector2(0.5f, 1);
        rr.anchoredPosition = Vector2.zero;
        rr.sizeDelta = new Vector2(0, 80);
        var nameText = NewText("Name", rr, character.ToUpper(), 48, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(nameText.rectTransform);
        nameText.rectTransform.offsetMin = new Vector2(0, 10);

        var roleText = NewText("Role", info, role, 40, new Color32(110, 50, 5, 255), FontStyles.Italic | FontStyles.Bold, TextAlignmentOptions.Left);
        roleText.rectTransform.anchorMin = new Vector2(0, 1);
        roleText.rectTransform.anchorMax = new Vector2(1, 1);
        roleText.rectTransform.pivot = new Vector2(0.5f, 1);
        roleText.rectTransform.anchoredPosition = new Vector2(0, -96);
        roleText.rectTransform.sizeDelta = new Vector2(0, 50);

        var desc = NewText("Description", info, description, 38, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.rectTransform.anchorMin = Vector2.zero;
        desc.rectTransform.anchorMax = new Vector2(1, 1);
        desc.rectTransform.offsetMin = Vector2.zero;
        desc.rectTransform.offsetMax = new Vector2(0, -146);
    }

    // ---- background decoration ---------------------------------------------------------------
    static RectTransform ArtItem(RectTransform art, string name, float tileX, float tileY, Vector2 size)
    {
        var rt = NewRect(name, art);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(tileX * Tile, tileY * Tile);
        rt.sizeDelta = size;
        return rt;
    }

    static void AddDecor(RectTransform art, string name, Sprite sprite, float tx, float ty, float amp, float period, float phase)
    {
        var rt = ArtItem(art, name, tx, ty, new Vector2(Tile, Tile));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        rt.gameObject.AddComponent<MenuDecor>().Configure(MenuDecor.Mode.Bob, amp, period, phase, 0f);
    }

    static void AddCloud(RectTransform art, string name, System.Func<int, Sprite> sheet, float tx, float ty, float speed, float phase)
    {
        var rt = ArtItem(art, name, tx, ty, new Vector2(Tile * 3, Tile));
        for (int i = 0; i < 3; i++)
        {
            var piece = NewImage("Piece" + i, rt, sheet(153 + i));
            piece.rectTransform.anchorMin = piece.rectTransform.anchorMax = new Vector2(0, 0.5f);
            piece.rectTransform.pivot = new Vector2(0, 0.5f);
            piece.rectTransform.anchoredPosition = new Vector2(i * Tile, 0);
            piece.rectTransform.sizeDelta = new Vector2(Tile, Tile);
        }
        rt.gameObject.AddComponent<MenuDecor>().Configure(MenuDecor.Mode.Drift, 0f, 1f, phase, speed * 4f);
    }
}
