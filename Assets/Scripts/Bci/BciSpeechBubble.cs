using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "character speech bubble": a character on the left and a speech bubble next to it, whose text appears letter by letter.
// All the instructions of the BCI stages are said through it. The picture of the character is BciSettings.CharacterSprite (put your
// sprite there); until there is one, Ralsei of the dance frames speaks.
//
//     var bubble = BciSpeechBubble.Create(canvasTransform, new Vector2(0f, 0f), new Vector2(60f, 50f));
//     bubble.Say("Hello!");
public class BciSpeechBubble : MonoBehaviour
{
    const float LettersPerSecond = 55f;

    RectTransform characterRect;
    Image characterImage;
    TMP_Text label;
    Image bubbleFrame, bubbleFace, tail;
    Coroutine typing;
    string current = "";
    float dim = 1f;

    // `anchor` is the corner of the screen (0,0 = bottom left, 1,0 = bottom right ...) the group sticks to, at `offset` from it.
    public static BciSpeechBubble Create(Transform canvas, Vector2 anchor, Vector2 offset, float bubbleWidth = 1000f, float dim = 1f)
    {
        var root = BciUi.Rect("Speech Bubble", canvas);
        var bubble = root.gameObject.AddComponent<BciSpeechBubble>();
        bubble.Build(root, anchor, offset, bubbleWidth);
        bubble.SetDim(dim);
        return bubble;
    }

    static Sprite FindCharacterSprite()
    {
        var chosen = BciSettings.Instance.CharacterSprite;
        if (chosen != null) return chosen;
        var fromResources = Resources.Load<Sprite>("Bci/character");              // Assets/Resources/Bci/character.png, if you add one
        if (fromResources != null) return fromResources;
        var art = Resources.Load<CelebrationArt>("CelebrationArt");
        return art != null && art.ralsei.dance != null && art.ralsei.dance.Length > 0 ? art.ralsei.dance[0] : null;
    }

    void Build(RectTransform root, Vector2 anchor, Vector2 offset, float bubbleWidth)
    {
        const float characterHeight = 260f;
        var sprite = FindCharacterSprite();
        float aspect = sprite != null ? sprite.rect.width / sprite.rect.height : 0.7f;
        float characterWidth = characterHeight * aspect;
        float bubbleHeight = 230f;

        root.sizeDelta = new Vector2(characterWidth + 40f + bubbleWidth, Mathf.Max(characterHeight, bubbleHeight));
        BciUi.Anchor(root, anchor, offset);
        root.pivot = anchor;

        // the character, at the left (feet on the bottom edge)
        characterRect = BciUi.Rect("Character", root);
        characterRect.anchorMin = characterRect.anchorMax = new Vector2(0f, 0f);
        characterRect.pivot = new Vector2(0f, 0f);
        characterRect.sizeDelta = new Vector2(characterWidth, characterHeight);
        characterRect.anchoredPosition = Vector2.zero;
        characterImage = characterRect.gameObject.AddComponent<Image>();
        characterImage.sprite = sprite;
        characterImage.raycastTarget = false;
        characterImage.enabled = sprite != null;

        // the bubble, at the right of the character
        float bx = characterWidth + 40f;
        var tailRect = BciUi.Rect("Tail", root);
        tailRect.anchorMin = tailRect.anchorMax = new Vector2(0f, 0f);
        tailRect.pivot = new Vector2(0.5f, 0.5f);
        tailRect.sizeDelta = new Vector2(34f, 34f);
        tailRect.anchoredPosition = new Vector2(bx - 2f, 110f);
        tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        tail = tailRect.gameObject.AddComponent<Image>();
        tail.color = BciUi.Cream;
        tail.raycastTarget = false;

        bubbleFrame = BciUi.Panel("Bubble frame", root, new Vector2(bubbleWidth, bubbleHeight), Color.black, Vector2.zero);
        var frameRect = bubbleFrame.rectTransform;
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0f, 0f);
        frameRect.pivot = new Vector2(0f, 0f);
        frameRect.anchoredPosition = new Vector2(bx, 30f);
        bubbleFace = BciUi.Panel("Bubble", frameRect, new Vector2(bubbleWidth - 12f, bubbleHeight - 12f), BciUi.Cream, Vector2.zero);

        label = BciUi.Text("Say", bubbleFace.transform, "", 40f, Vector2.zero, new Vector2(bubbleWidth - 70f, bubbleHeight - 40f),
                           BciUi.Ink, TextAlignmentOptions.Left, true);
        label.fontSizeMin = 26f;
        label.fontSizeMax = 40f;
        label.enableAutoSizing = true;

        // the tail's other half hides under the bubble: draw the bubble above it
        bubbleFrame.transform.SetAsLastSibling();
    }

    // The bubble and the character darker (for the black calibration screens, where a bright bubble would be a second light).
    public void SetDim(float brightness)
    {
        dim = Mathf.Clamp01(brightness);
        var c = new Color(dim, dim, dim, 1f);
        if (characterImage != null) characterImage.color = c;
        if (bubbleFace != null) bubbleFace.color = BciUi.Cream * dim;
        if (tail != null) tail.color = BciUi.Cream * dim;
        if (bubbleFrame != null) bubbleFrame.color = Color.black;
        if (label != null) label.color = BciUi.Ink;
    }

    public string Current => current;

    public void Say(string text, bool instant = false)
    {
        if (text == current) return;
        current = text;
        if (typing != null) StopCoroutine(typing);
        label.text = text;
        if (instant)
        {
            label.maxVisibleCharacters = int.MaxValue;
            return;
        }
        typing = StartCoroutine(Type());
    }

    IEnumerator Type()
    {
        label.maxVisibleCharacters = 0;
        label.ForceMeshUpdate();
        int total = label.textInfo.characterCount;
        float shown = 0f;
        while (shown < total)
        {
            shown += Time.unscaledDeltaTime * LettersPerSecond;
            label.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }
        label.maxVisibleCharacters = int.MaxValue;
        typing = null;
    }

    void Update()
    {
        // the character bobs a little, more while the words are still coming
        if (characterRect == null) return;
        float amount = typing != null ? 7f : 3f;
        float speed = typing != null ? 14f : 3f;
        characterRect.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(Time.unscaledTime * speed)) * amount);
    }
}
