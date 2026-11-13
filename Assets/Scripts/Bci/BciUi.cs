using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Small helpers to build the BCI screens in code (like the rest of the game's menus): canvases, text, buttons, an input box.
public static class BciUi
{
    public static readonly Color Ink = new Color(0.1f, 0.1f, 0.12f);
    public static readonly Color Cream = new Color(0.97f, 0.95f, 0.88f);
    public static readonly Color Gold = new Color(1f, 0.85f, 0.35f);
    public static readonly Color GoldHover = new Color(1f, 0.95f, 0.7f);
    public static readonly Color GoldPressed = new Color(0.9f, 0.7f, 0.2f);
    public static readonly Color Danger = new Color(0.72f, 0.14f, 0.08f);

    static TMP_FontAsset font;
    public static TMP_FontAsset Font => font != null ? font : font = Resources.Load<TMP_FontAsset>("Fonts/LilitaOne SDF");

    public static Canvas NewCanvas(string name, int sortingOrder, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    // The new screens need clicks: an EventSystem with the new Input System's UI module.
    public static void EnsureEventSystem() => LevelEnd.EnsureEventSystem();

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
    }

    public static Image Panel(string name, Transform parent, Vector2 size, Color color, Vector2 position)
    {
        var rect = Rect(name, parent);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text Text(string name, Transform parent, string text, float size, Vector2 position, Vector2 box, Color color,
                                TextAlignmentOptions align = TextAlignmentOptions.Center, bool wrap = false)
    {
        var rect = Rect(name, parent);
        rect.sizeDelta = box;
        rect.anchoredPosition = position;
        var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) t.font = Font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        return t;
    }

    // A black frame with a gold face and a label, like the buttons of the menus.
    public static Button NewButton(string label, Transform parent, Vector2 position, Vector2 size, float fontSize, Action onClick)
    {
        var frame = Panel(label + " frame", parent, size, Color.black, position);
        var face = Panel(label + " button", frame.transform, size - new Vector2(12f, 12f), Gold, Vector2.zero);
        face.raycastTarget = true;
        var button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(GoldHover.r / Gold.r, GoldHover.g / Gold.g, GoldHover.b / Gold.b, 1f);
        colors.pressedColor = new Color(GoldPressed.r / Gold.r, GoldPressed.g / Gold.g, GoldPressed.b / Gold.b, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        button.colors = colors;
        if (onClick != null) button.onClick.AddListener(() => onClick());
        Text("Text", face.transform, label, fontSize, Vector2.zero, size - new Vector2(20f, 20f), Ink);
        return button;
    }

    // Built switched off and switched on last, so the TMP input field is wired up before it starts.
    public static TMP_InputField NewInput(Transform parent, Vector2 position, Vector2 size, string placeholderText, int characterLimit)
    {
        var rect = Rect("Input", parent);
        rect.gameObject.SetActive(false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var background = rect.gameObject.AddComponent<Image>();
        background.color = Color.white;
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);

        var area = Rect("Text Area", rect);
        Stretch(area);
        area.offsetMin = new Vector2(16f, 6f);
        area.offsetMax = new Vector2(-16f, -6f);
        area.gameObject.AddComponent<RectMask2D>();
        var text = Text("Text", area, "", 52f, Vector2.zero, Vector2.zero, Ink, TextAlignmentOptions.Left);
        Stretch(text.rectTransform);
        var placeholder = Text("Placeholder", area, placeholderText, 52f, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.3f), TextAlignmentOptions.Left);
        Stretch(placeholder.rectTransform);

        var field = rect.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = background;
        field.characterLimit = characterLimit;
        rect.gameObject.SetActive(true);
        return field;
    }
}
