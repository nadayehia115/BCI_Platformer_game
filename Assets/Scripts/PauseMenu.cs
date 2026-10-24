using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pause: a PAUSE button in the top right corner of GameScene (or the Esc / P key, or Start on a gamepad) stops the game and shows a
// panel with RESUME and EXIT (EXIT goes back to the main menu). While the game is paused the clock stands still (the level clock
// counts game time) and the S / T keys of the BCI flow do nothing.
//
// Like ScoreUI it builds its own canvases whenever GameScene loads, so no scene has to be changed. The buttons are clicked by
// reading the mouse here (not through an EventSystem): GameScene has none until the end screen, and one would make the HUD
// pictures clickable. Nothing can be paused once the end screen is up (it has its own buttons).
public class PauseMenu : MonoBehaviour
{
    const string GameSceneName = "GameScene";
    const string MenuSceneName = "MainMenu";
    const string FontResource = "Fonts/LilitaOne SDF";

    static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
    static readonly Color GoldHover = new Color(1f, 0.95f, 0.7f);
    static readonly Color Cream = new Color(0.97f, 0.95f, 0.88f);

    // read by KrisForms and CharacterSwitcher: no fountain and no power while the game is paused
    public static bool IsPaused { get; private set; }

    TMP_FontAsset font;
    Image pauseFace, resumeFace, exitFace;
    GameObject pauseButton, panel;
    float savedScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        IsPaused = false;
        if (scene.name != GameSceneName || FindFirstObjectByType<PauseMenu>() != null) return;
        new GameObject("Pause Menu").AddComponent<PauseMenu>();
    }

    // ---- building ----

    void Awake()
    {
        font = Resources.Load<TMP_FontAsset>(FontResource);

        // the button in the corner (above the HUD, below the end screen)
        var hud = NewCanvas(transform, "Pause Button Canvas", 60);
        var frame = NewImage("Pause frame", hud, new Vector2(116f, 116f), Color.black);
        var frameRect = frame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = frameRect.pivot = new Vector2(1f, 1f);
        frameRect.anchoredPosition = new Vector2(-40f, -32f);
        pauseButton = frame.gameObject;
        pauseFace = NewImage("Pause button", frame.transform, new Vector2(104f, 104f), Gold);
        NewImage("Bar 1", pauseFace.transform, new Vector2(16f, 54f), Color.black).rectTransform.anchoredPosition = new Vector2(-19f, 0f);
        NewImage("Bar 2", pauseFace.transform, new Vector2(16f, 54f), Color.black).rectTransform.anchoredPosition = new Vector2(19f, 0f);

        // the panel, shown while the game is paused
        var overlay = NewCanvas(transform, "Pause Panel Canvas", 90);
        panel = overlay.gameObject;
        var dim = NewImage("Dim", overlay, new Vector2(4000f, 3000f), new Color(0f, 0f, 0.05f, 0.7f));
        dim.raycastTarget = true;
        var panelFrame = NewImage("Panel frame", overlay, new Vector2(1000f, 760f), Color.black);
        var back = NewImage("Panel", panelFrame.transform, new Vector2(980f, 740f), Cream);
        NewLabel("Title", back.transform, "PAUSED", 150f, new Vector2(0f, 235f), new Vector2(900f, 190f));
        resumeFace = NewButton("RESUME", back.transform, new Vector2(0f, 50f));
        exitFace = NewButton("EXIT", back.transform, new Vector2(0f, -110f));
        NewLabel("Hint", back.transform, "Esc: resume      M: main menu", 40f, new Vector2(0f, -265f), new Vector2(900f, 60f));
        panel.SetActive(false);
    }

    static Transform NewCanvas(Transform parent, string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        go.AddComponent<GraphicRaycaster>();                           // so the picture clicks behind it are blocked
        return go.transform;
    }

    static Image NewImage(string name, Transform parent, Vector2 size, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        var image = go.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    TMP_Text NewLabel(string name, Transform parent, string text, float size, Vector2 position, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = box;
        rect.anchoredPosition = position;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.black;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // A black frame with a gold face and the label on it; the face is the click area.
    Image NewButton(string label, Transform parent, Vector2 position)
    {
        var frame = NewImage(label + " frame", parent, new Vector2(520f, 120f), Color.black);
        frame.rectTransform.anchoredPosition = position;
        var face = NewImage(label + " button", frame.transform, new Vector2(508f, 108f), Gold);
        NewLabel("Text", face.transform, label, 60f, Vector2.zero, new Vector2(500f, 100f));
        return face;
    }

    // ---- input ----

    void Update()
    {
        if (LevelEnd.Instance != null && LevelEnd.Instance.Ended)       // the end screen has its own buttons
        {
            if (pauseButton.activeSelf) pauseButton.SetActive(false);
            return;
        }

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool toggle = (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame))
                      || (pad != null && pad.startButton.wasPressedThisFrame);

        if (!IsPaused)
        {
            pauseFace.color = Over(pauseFace) ? GoldHover : Gold;
            if (toggle || Clicked(pauseFace)) Pause();
            return;
        }

        resumeFace.color = Over(resumeFace) ? GoldHover : Gold;
        exitFace.color = Over(exitFace) ? GoldHover : Gold;
        bool enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
        if (toggle || enter || Clicked(resumeFace)) Resume();
        else if ((kb != null && kb.mKey.wasPressedThisFrame) || Clicked(exitFace)) ExitToMenu();
    }

    static bool Over(Image face)
    {
        var mouse = Mouse.current;
        return mouse != null && RectTransformUtility.RectangleContainsScreenPoint(face.rectTransform, mouse.position.ReadValue(), null);
    }

    static bool Clicked(Image face) => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Over(face);

    // ---- what the buttons do ----

    void Pause()
    {
        IsPaused = true;
        savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        RemoteInput.SetHorizontal(0f);                                  // Kris stands still (the Python heartbeat starts him again after RESUME)
        panel.SetActive(true);
        pauseButton.SetActive(false);
        AudioListener.pause = true;                                     // a sound that is playing (the fountain) waits too
    }

    void Resume()
    {
        IsPaused = false;
        Time.timeScale = savedScale;
        panel.SetActive(false);
        pauseButton.SetActive(true);
        AudioListener.pause = false;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBack();
    }

    void ExitToMenu()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayClick();
        if (Application.CanStreamedLevelBeLoaded(MenuSceneName)) SceneManager.LoadScene(MenuSceneName);
        else SceneManager.LoadScene(SceneManager.GetActiveScene().name);    // the menu is not in the build (testing in the editor)
    }

    void OnDestroy()
    {
        if (IsPaused)                                                   // the scene is going away while paused
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
        IsPaused = false;
    }
}
