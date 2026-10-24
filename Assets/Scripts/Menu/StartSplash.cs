using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The start screen: when the game is started, the logo picture (Resources/StartLogo), the name of the game, the team and the
// copyright notice of the game it is based on are shown over the main menu for a few seconds (any key, mouse button or gamepad button
// takes it away at once), then it goes away. It is shown once each time the game is started, not every time the player comes back to the
// main menu.
//
// It fades in from black and out to black (a black curtain over it changes, the picture itself does not): the picture has its own
// background, so fading the picture and the background separately would show its rectangle as a darker box.
//
// Like SsvepMenu it is built in code when the main menu loads, so no scene has to be changed. While it is on the screen the menu
// below cannot be clicked or selected (the EventSystem is switched off), so the key that skips it cannot press a button.
public class StartSplash : MonoBehaviour
{
    const string MenuSceneName = "MainMenu";
    const string FontResource = "Fonts/LilitaOne SDF";
    const string LogoResource = "StartLogo";

    // ---- the words on the screen: change them here ----
    const string GameName = "BCI BUSTERS";
    const string Team = "Abdelrahman Amro, Eyad Mohamed and Nada Yehia";
    const string OriginalGame = "DELTARUNE";
    const string OriginalCreator = "Toby Fox";

    const float FadeIn = 0.7f, Hold = 11f, FadeOut = 0.7f, SkipFade = 0.2f, Reveal = 0.3f;     // long enough to read all the lines
    const float NoSkipBefore = 0.6f;                                   // a key (or click) before this time is ignored: it is the one that started the game
    const float LogoHeight = 600f;                                     // in canvas units (the canvas is 1920 x 1080)

    static readonly Color Back = new Color32(60, 71, 83, 255);         // the picture's own background colour, so its edges disappear
    static readonly Color TitleColour = new Color(0.62f, 0.95f, 1f);
    static readonly Color TeamColour = Color.white;
    static readonly Color OriginalColour = new Color(0.78f, 0.85f, 0.9f);
    static readonly Color SmallColour = new Color(0.8f, 0.86f, 0.9f);

    static bool shown;

    TMP_FontAsset font;
    GameObject content;                                                // everything but the curtain
    Image curtain;                                                     // black, on top of everything
    TMP_Text hint;
    EventSystem blockedEvents;
    IDisposable anyButton;
    bool pressed;
    float age, leaveAt = -1f, leaveFrom = 1f, fadeOut = FadeOut;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (shown || SceneManager.GetActiveScene().name != MenuSceneName) return;     // only when the game starts in the main menu
        shown = true;
        new GameObject("Start Splash").AddComponent<StartSplash>().Build();
    }

    // ---- building ----

    void Build()
    {
        font = Resources.Load<TMP_FontAsset>(FontResource);

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;                                    // above everything of the menu
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var contentRect = NewRect("Content", transform);
        Stretch(contentRect);
        content = contentRect.gameObject;

        var back = NewRect("Background", contentRect);
        Stretch(back);
        back.gameObject.AddComponent<Image>().color = Back;            // also catches the clicks

        var logo = Resources.Load<Sprite>(LogoResource);
        if (logo != null)
        {
            var picture = NewRect("Logo", contentRect);
            picture.anchorMin = picture.anchorMax = picture.pivot = new Vector2(0.5f, 1f);
            picture.sizeDelta = new Vector2(LogoHeight * logo.rect.width / logo.rect.height, LogoHeight);
            picture.anchoredPosition = new Vector2(0f, -10f);
            var image = picture.gameObject.AddComponent<Image>();
            image.sprite = logo;
            image.raycastTarget = false;
        }

        NewText("Game Name", GameName, 100f, -765f, 1700f, TitleColour, FontStyles.Bold);
        NewText("Team", "A game by " + Team, 38f, -842f, 1700f, TeamColour);
        NewText("Original Game", "Based on " + OriginalGame + ", created by " + OriginalCreator, 34f, -886f, 1700f, OriginalColour);
        NewText("Copyright 1", OriginalGame + ", its characters and its music are © " + OriginalCreator + ".", 26f, -944f, 1700f, SmallColour);
        NewText("Copyright 2", "This is an unofficial fan project and is not affiliated with " + OriginalCreator + ".", 26f, -978f, 1700f, SmallColour);

        hint = NewText("Hint", "press any key", 26f, 0f, 600f, SmallColour);
        var hintRect = hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(1f, 0f);
        hintRect.anchoredPosition = new Vector2(-40f, 24f);
        hint.alignment = TextAlignmentOptions.Right;

        var curtainRect = NewRect("Curtain", transform);
        Stretch(curtainRect);
        curtain = curtainRect.gameObject.AddComponent<Image>();
        curtain.color = Color.black;                                   // the screen starts black and fades in

        blockedEvents = EventSystem.current;
        if (blockedEvents != null) blockedEvents.enabled = false;
        anyButton = InputSystem.onAnyButtonPress.CallOnce(_ => pressed = true);       // any key, mouse button, gamepad button, touch
    }

    // ---- time ----

    void Update()
    {
        age += Mathf.Min(Time.unscaledDeltaTime, 0.1f);                 // a hitch while the menu loads must not use up the time

        float dark = 1f - Mathf.Clamp01(age / FadeIn);                  // how black the curtain is
        if (age < NoSkipBefore) pressed = false;
        bool skip = age >= NoSkipBefore && (pressed || SkipPressed());
        if (leaveAt < 0f && (skip || age >= FadeIn + Hold))
        {
            leaveAt = age;
            leaveFrom = dark;
            if (skip) fadeOut = SkipFade;
        }
        if (leaveAt >= 0f)
        {
            float since = age - leaveAt;
            if (since < fadeOut) dark = Mathf.Lerp(leaveFrom, 1f, since / fadeOut);          // the screen goes black ...
            else
            {
                content.SetActive(false);                                                    // ... the start screen is taken away ...
                float lift = (since - fadeOut) / Reveal;                                     // ... and the curtain lifts from the menu
                if (lift >= 1f) { Destroy(gameObject); return; }
                dark = 1f - lift;
            }
        }
        curtain.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, dark));
        if (hint != null) hint.alpha = 0.45f + 0.35f * Mathf.Sin(age * 3f);
    }

    static bool SkipPressed()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var pad = Gamepad.current;
        return (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
               || (mouse != null && mouse.leftButton.wasPressedThisFrame)
               || (pad != null && pad.buttonSouth.wasPressedThisFrame);
    }

    void OnDestroy()
    {
        anyButton?.Dispose();
        if (blockedEvents != null) blockedEvents.enabled = true;       // the menu works again
    }

    // ---- small helpers ----

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // A one line text, centred on the screen horizontally, `y` canvas units below the top (negative = down).
    TMP_Text NewText(string name, string text, float size, float y, float width, Color colour, FontStyles style = FontStyles.Normal)
    {
        var rect = NewRect(name, content.transform);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, size * 1.3f);
        rect.anchoredPosition = new Vector2(0f, y + size * 0.65f);
        var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.color = colour;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
