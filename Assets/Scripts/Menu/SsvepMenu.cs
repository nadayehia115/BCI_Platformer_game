using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The SETTINGS button of the main menu and the SSVEP tab it opens. The tab shows
//   * the refresh rate of the screen the game runs on and the flicker frequency of the pad the game worked out for it by itself
//     (SsvepSettings.Auto), and lets the player choose the frequency by hand instead (MANUAL): what is typed is checked against
//     the rules (not 8-25 Hz, and a rate the screen can really show) and the nearest allowed one is used, with a message why;
//   * the size of the pad: its diameter in centimetres (3 by default).
// What is chosen is saved (PlayerPrefs) and used by the pad in the game (SsvepBoard / the SsvepPad prefab) and by the
// "frequencies" answer to the Python side. There is ONE pad (it means "activate power"), so one frequency.
//
// Like HighScoreBoard it is built in code every time the main menu loads (a fourth button cloned from HOW TO PLAY, and the tab
// as an overlay on top of the menu), so the menu scene itself does not have to be rebuilt for it.
public class SsvepMenu : MonoBehaviour
{
    const string MenuSceneName = "MainMenu";
    const string FontResource = "Fonts/LilitaOne SDF";

    static readonly Color Ink = new Color(0.1f, 0.1f, 0.12f);
    static readonly Color Cream = new Color(0.97f, 0.95f, 0.88f);
    static readonly Color Dimmed = new Color(0.6f, 0.6f, 0.6f);
    static readonly Color Grey = new Color(0.4f, 0.4f, 0.4f);
    static readonly Color Warn = new Color(0.72f, 0.14f, 0.08f);

    TMP_FontAsset font;
    CanvasGroup mainGroup;
    GameObject overlay, opener, closeButton;
    TMP_Text refreshText, messageText, autoText, usedText;
    TMP_Text freqPlaceholder, sizePlaceholder;
    Image autoButtonImage, manualButtonImage;
    TMP_InputField freqInput, sizeInput;
    string freqMessage = "", sizeMessage = "";
    float shownRefresh;
    bool refreshing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != MenuSceneName) return;
        var main = GameObject.Find("MainPanel");
        var how = GameObject.Find("HowToPlayButton");
        if (main == null || how == null || how.transform.parent.Find("SettingsButton") != null) return;

        var go = new GameObject("SSVEP Settings", typeof(RectTransform));
        go.transform.SetParent(main.transform.parent, false);
        go.AddComponent<SsvepMenu>().Build(main.transform, how);
    }

    // ---- building ----

    void Build(Transform mainPanel, GameObject howTemplate)
    {
        font = Resources.Load<TMP_FontAsset>(FontResource);
        mainGroup = mainPanel.GetComponent<CanvasGroup>();

        // room for a fourth button in the stack of buttons
        var stack = (RectTransform)howTemplate.transform.parent;
        if (stack.GetComponent<VerticalLayoutGroup>() != null)
        {
            stack.sizeDelta = new Vector2(stack.sizeDelta.x, 600f);
            stack.anchoredPosition = new Vector2(stack.anchoredPosition.x, -125f);
        }
        var open = CloneButton(howTemplate, stack, "SettingsButton", "SETTINGS", new Vector2(640f, 130f), 64f);
        open.transform.SetSiblingIndex(howTemplate.transform.GetSiblingIndex() + 1);
        open.onClick.AddListener(Open);
        opener = open.gameObject;

        BuildPanel(mainPanel.parent, howTemplate);
        overlay.SetActive(false);
    }

    void BuildPanel(Transform parent, GameObject buttonTemplate)
    {
        var overlayRect = NewRect("SSVEP Overlay", parent);
        overlay = overlayRect.gameObject;
        Stretch(overlayRect);
        var dim = overlay.AddComponent<Image>();                                   // darkens the menu and catches clicks
        dim.color = new Color(0f, 0f, 0f, 0.62f);

        var frame = NewImage("Frame", overlay.transform, new Vector2(1240f, 720f), Color.black);
        var panelBack = NewImage("Panel", frame.transform, new Vector2(1220f, 700f), Cream).transform;
        var content = NewRect("Content", panelBack);                                // everything below is a child of this, moved down to centre it
        content.sizeDelta = new Vector2(1220f, 840f);
        content.anchoredPosition = new Vector2(0f, -67f);
        Transform panel = content;

        NewText("Title", panel, "SSVEP SETTINGS", 64f, new Vector2(0f, 365f), new Vector2(1100f, 80f), Ink);
        refreshText = NewText("Refresh Rate", panel, "", 38f, new Vector2(0f, 305f), new Vector2(1100f, 56f), Ink);
        NewText("Rules", panel,
            "One pad flickers on the screen: it means ACTIVATE POWER. Its frequency is never 8-14 Hz (alpha rhythm of the brain) " +
            "or 15-25 Hz (epilepsy range), and only rates your screen can really show are possible.",
            25f, new Vector2(0f, 250f), new Vector2(1120f, 66f), Grey, wrap: true);

        // ---- the frequency ----
        var auto = CloneButton(buttonTemplate, panel, "AutomaticButton", "AUTOMATIC", new Vector2(380f, 80f), 44f);
        Place(auto, new Vector2(-210f, 172f));
        auto.onClick.AddListener(() => ChooseMode(false));
        autoButtonImage = auto.GetComponent<Image>();
        var manual = CloneButton(buttonTemplate, panel, "ManualButton", "MANUAL", new Vector2(380f, 80f), 44f);
        Place(manual, new Vector2(210f, 172f));
        manual.onClick.AddListener(() => ChooseMode(true));
        manualButtonImage = manual.GetComponent<Image>();

        NewText("Head Auto", panel, "AUTOMATIC", 28f, new Vector2(-150f, 112f), new Vector2(280f, 36f), Grey);
        NewText("Head Manual", panel, "MANUAL (Hz)", 28f, new Vector2(110f, 112f), new Vector2(220f, 36f), Grey);
        NewText("Head Used", panel, "USED IN THE GAME", 28f, new Vector2(400f, 112f), new Vector2(320f, 36f), Grey);

        var row = NewImage("Frequency Row", panel, new Vector2(1160f, 78f), new Color(1f, 1f, 1f, 0.55f));
        row.rectTransform.anchoredPosition = new Vector2(0f, 58f);
        NewText("Name", row.transform, "FREQUENCY", 36f, new Vector2(-470f, 0f), new Vector2(240f, 70f), Ink);
        autoText = NewText("Automatic", row.transform, "", 44f, new Vector2(-150f, 0f), new Vector2(280f, 70f), Ink);
        freqInput = NewInput(row.transform, new Vector2(110f, 0f), new Vector2(200f, 62f), out freqPlaceholder);
        freqInput.onEndEdit.AddListener(OnFrequencyTyped);
        usedText = NewText("Used", row.transform, "", 44f, new Vector2(400f, 0f), new Vector2(340f, 76f), Ink);

        // ---- the size of the pad ----
        NewText("Size Title", panel, "SIZE OF THE PAD", 28f, new Vector2(-200f, -10f), new Vector2(700f, 36f), Grey);
        var sizeRow = NewImage("Size Row", panel, new Vector2(800f, 74f), new Color(1f, 1f, 1f, 0.55f));
        sizeRow.rectTransform.anchoredPosition = new Vector2(-200f, -68f);
        NewText("Name", sizeRow.transform, "DIAMETER (cm)", 34f, new Vector2(-200f, 0f), new Vector2(360f, 66f), Ink);
        sizeInput = NewInput(sizeRow.transform, new Vector2(170f, 0f), new Vector2(200f, 58f), out sizePlaceholder);
        sizeInput.onEndEdit.AddListener(OnSizeTyped);

        messageText = NewText("Message", panel, "", 25f, new Vector2(0f, -150f), new Vector2(1120f, 64f), Warn, wrap: true);

        var close = CloneButton(buttonTemplate, panel, "CloseButton", "CLOSE", new Vector2(320f, 80f), 48f);
        Place(close, new Vector2(0f, -232f));
        close.onClick.AddListener(Close);
        closeButton = close.gameObject;
    }

    // A copy of an existing menu button (same look, hover animation and sounds) with its own label and nothing wired to it.
    Button CloneButton(GameObject template, Transform parent, string name, string label, Vector2 size, float fontSize)
    {
        var go = Instantiate(template, parent);
        go.name = name;
        var rect = (RectTransform)go.transform;
        rect.localScale = Vector3.one;
        rect.sizeDelta = size;
        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        var text = go.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.text = label;
            text.fontSize = fontSize;
        }
        return button;
    }

    static void Place(Button button, Vector2 position)
    {
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
    }

    // ---- opening and closing ----

    public void Open()
    {
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        if (mainGroup != null) mainGroup.interactable = false;               // the buttons under the tab cannot be reached by keyboard
        Refresh();
        Select(closeButton);
    }

    public void Close()
    {
        overlay.SetActive(false);
        if (mainGroup != null) mainGroup.interactable = true;
        Select(opener);
    }

    static void Select(GameObject go)
    {
        if (go != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go);
    }

    bool Typing() =>
        (freqInput != null && freqInput.isFocused) || (sizeInput != null && sizeInput.isFocused);

    void Update()
    {
        if (!overlay.activeSelf) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && !Typing()) Close();
        if (!Mathf.Approximately(shownRefresh, SsvepSettings.RefreshForPlanning)) Refresh();   // the game measured the real rate
    }

    // ---- choices ----

    void ChooseMode(bool manual)
    {
        if (manual && SsvepSettings.ManualWanted(0) <= 0f)
        {
            // the first time, the field starts with what the automatic mode found, so there is something sensible to change
            var auto = SsvepSettings.Auto(SsvepSettings.RefreshForPlanning, 1);
            if (auto.Length > 0) SsvepSettings.SetManualWanted(0, auto[0].frequency);
        }
        SsvepSettings.Manual = manual;
        freqMessage = "";
        Refresh();
    }

    static bool TryNumber(string text, out float value) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    void OnFrequencyTyped(string text)
    {
        if (refreshing) return;
        if (!TryNumber(text, out float hz) || hz < 1f || hz > 200f)
        {
            freqMessage = "Frequency: type a number of Hz, for example 6.5.";
            Refresh();
            return;
        }

        float refresh = SsvepSettings.RefreshForPlanning;
        SsvepSettings.SetManualWanted(0, hz);
        var snapped = SsvepSettings.Snap(hz, refresh);
        freqMessage = "";
        if (!SsvepSettings.Allowed(hz))
        {
            string reason = hz < 14.5f ? "alpha band, 8-14 Hz" : "epilepsy range, 15-25 Hz";
            freqMessage = $"{hz:0.##} Hz is not allowed ({reason}). Using {snapped.frequency:0.##} Hz.";
        }
        else if (Mathf.Abs(snapped.frequency - hz) > 0.05f)
        {
            freqMessage = $"This screen cannot show {hz:0.##} Hz. Using the nearest, {snapped.frequency:0.##} Hz.";
        }
        Refresh();
    }

    void OnSizeTyped(string text)
    {
        if (refreshing) return;
        sizeMessage = "";
        if (!TryNumber(text, out float cm))
        {
            sizeMessage = $"Diameter: type a number of centimetres, for example {SsvepSettings.DefaultPadCm:0}.";
        }
        else
        {
            SsvepSettings.PadCm = cm;
            if (cm < SsvepSettings.MinPadCm || cm > SsvepSettings.MaxPadCm)
                sizeMessage = $"The diameter is kept between {SsvepSettings.MinPadCm:0} and {SsvepSettings.MaxPadCm:0} cm: using {SsvepSettings.PadCm:0.##} cm.";
        }
        Refresh();
    }

    // ---- showing the current state ----

    void Refresh()
    {
        refreshing = true;
        float refresh = SsvepSettings.RefreshForPlanning;
        shownRefresh = refresh;
        bool manual = SsvepSettings.Manual;
        var auto = SsvepSettings.Auto(refresh, 1);
        var used = SsvepSettings.Plan(1)[0];

        refreshText.text = $"Your screen refreshes at {refresh:0.#} Hz" + (SsvepSettings.MeasuredRefresh > 0f ? " (measured while the game runs)" : "");
        autoButtonImage.color = manual ? Dimmed : Color.white;                   // the chosen mode is bright, the other one dimmed
        manualButtonImage.color = manual ? Color.white : Dimmed;

        float found = auto.Length > 0 ? auto[0].frequency : used.frequency;
        autoText.text = found.ToString("0.0#", CultureInfo.InvariantCulture) + " Hz";
        autoText.color = manual ? Grey : Ink;

        freqInput.interactable = manual;
        freqPlaceholder.text = found.ToString("0.0#", CultureInfo.InvariantCulture);
        float wanted = SsvepSettings.ManualWanted(0);
        if (!freqInput.isFocused)
            freqInput.SetTextWithoutNotify(manual && wanted > 0f ? wanted.ToString("0.##", CultureInfo.InvariantCulture) : "");
        usedText.text = used.frequency.ToString("0.0#", CultureInfo.InvariantCulture) + " Hz\n" +
                        "<size=55%>colour changes every " + (used.halfCycleFrames == 1 ? "frame" : used.halfCycleFrames + " frames") + "</size>";

        // the size
        if (!sizeInput.isFocused) sizeInput.SetTextWithoutNotify(SsvepSettings.PadCm.ToString("0.##", CultureInfo.InvariantCulture));
        sizePlaceholder.text = SsvepSettings.DefaultPadCm.ToString("0", CultureInfo.InvariantCulture);

        string notes = freqMessage.Length > 0 && sizeMessage.Length > 0 ? freqMessage + "\n" + sizeMessage : freqMessage + sizeMessage;
        messageText.text = notes;
        refreshing = false;
    }

    // ---- small UI helpers ----

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

    static Image NewImage(string name, Transform parent, Vector2 size, Color colour)
    {
        var rect = NewRect(name, parent);
        rect.sizeDelta = size;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    TMP_Text NewText(string name, Transform parent, string text, float size, Vector2 position, Vector2 box, Color colour, bool wrap = false)
    {
        var rect = NewRect(name, parent);
        rect.sizeDelta = box;
        rect.anchoredPosition = position;
        var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = colour;
        t.raycastTarget = false;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return t;
    }

    // A text box for typing a number. Built by hand and switched on last, so it is wired up before it starts.
    TMP_InputField NewInput(Transform parent, Vector2 position, Vector2 size, out TMP_Text placeholder)
    {
        var rect = NewRect("Input", parent);
        rect.gameObject.SetActive(false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var background = rect.gameObject.AddComponent<Image>();
        background.color = Color.white;
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);

        var area = NewRect("Text Area", rect);
        Stretch(area);
        area.offsetMin = new Vector2(10f, 4f);
        area.offsetMax = new Vector2(-10f, -4f);
        area.gameObject.AddComponent<RectMask2D>();
        var text = NewText("Text", area, "", 40f, Vector2.zero, Vector2.zero, Ink);
        Stretch(text.rectTransform);
        placeholder = NewText("Placeholder", area, "", 40f, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.3f));
        Stretch(placeholder.rectTransform);

        var field = rect.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = background;
        field.characterLimit = 7;
        rect.gameObject.SetActive(true);
        return field;
    }
}
