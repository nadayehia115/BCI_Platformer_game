using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "character speech bubble": a character on the left and a speech bubble next to it, whose text appears letter by letter.
// The instructions of the setup stages (contrast, headset, loading) are said through it - NOT the calibration / training screens, where
// the player must be able to concentrate. The speaker is Tenna (BciArt, Assets/deltarune); every time his words are typed his voice
// (Resources/Audio/tenna_voice.mp3, the sound of the WhatsApp video) plays, and stops when the text is complete. BciSettings.CharacterSprite
// overrides the picture; without Tenna's art Ralsei of the dance frames speaks.
//
//     var bubble = BciSpeechBubble.Create(canvasTransform, new Vector2(0f, 0f), new Vector2(60f, 50f));
//     bubble.Say("Hello!");
public class BciSpeechBubble : MonoBehaviour
{
    const float LettersPerSecond = 55f;
    public enum Pose { Talk, Point, Laugh }

    RectTransform characterRect;
    Image characterImage;
    TMP_Text label;
    Image bubbleFrame, bubbleFace, tail;
    Coroutine typing;
    string current = "";
    float dim = 1f;
    AudioSource voice;
    static AudioClip voiceClip;

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
        var bci = Resources.Load<BciArt>("BciArt");
        if (bci != null && bci.tennaTalk != null) return bci.tennaTalk;
        var art = Resources.Load<CelebrationArt>("CelebrationArt");
        return art != null && art.ralsei.dance != null && art.ralsei.dance.Length > 0 ? art.ralsei.dance[0] : null;
    }

    void Build(RectTransform root, Vector2 anchor, Vector2 offset, float bubbleWidth)
    {
        const float characterHeight = 260f;
        var sprite = FindCharacterSprite();
        float characterWidth = 230f;                          // a fixed box: every pose is fitted into it (preserveAspect), the bubble never moves
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
        characterImage.preserveAspect = true;
        characterImage.enabled = sprite != null;

        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.loop = true;
        voice.spatialBlend = 0f;
        if (voiceClip == null) voiceClip = Resources.Load<AudioClip>("Audio/tenna_voice");
        voice.clip = voiceClip;

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

    // Tenna's pose: Talk (finger up, the normal one), Point (at the player), Laugh (good news).
    public void SetPose(Pose pose)
    {
        var art = Resources.Load<BciArt>("BciArt");
        if (art == null || characterImage == null || BciSettings.Instance.CharacterSprite != null) return;
        var sprite = pose == Pose.Point ? art.tennaPoint : pose == Pose.Laugh ? art.tennaLaugh : art.tennaTalk;
        if (sprite != null) characterImage.sprite = sprite;
    }

    void StartVoice()
    {
        if (voice == null || voice.clip == null) return;
        var audio = AudioManager.Instance;
        voice.volume = audio != null ? (audio.Muted ? 0f : audio.SfxVolume) : 0.8f;
        voice.time = Random.Range(0f, Mathf.Max(0f, voice.clip.length - 4f));       // another piece of the babble every time
        voice.Play();
    }

    void StopVoice()
    {
        if (voice != null && voice.isPlaying) voice.Stop();
    }

    public void Say(string text, bool instant = false)
    {
        if (text == current) return;
        current = text;
        if (typing != null) StopCoroutine(typing);
        StopVoice();
        label.text = text;
        if (instant)
        {
            label.maxVisibleCharacters = int.MaxValue;
            typing = null;
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
        StartVoice();                                          // Tenna talks while his words come
        while (shown < total)
        {
            shown += Mathf.Min(Time.unscaledDeltaTime, 0.05f) * LettersPerSecond;      // the first frame after a scene load is slow: it must not skip the words
            label.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }
        label.maxVisibleCharacters = int.MaxValue;
        typing = null;
        StopVoice();
    }

    void OnDisable() => StopVoice();

    void Update()
    {
        // the character bobs a little, more while the words are still coming
        if (characterRect == null) return;
        float amount = typing != null ? 7f : 3f;
        float speed = typing != null ? 14f : 3f;
        characterRect.anchoredPosition = new Vector2(0f, Mathf.Abs(Mathf.Sin(Time.unscaledTime * speed)) * amount);
    }
}
