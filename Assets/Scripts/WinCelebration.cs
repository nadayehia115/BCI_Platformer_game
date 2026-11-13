using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The level won celebration: when Kris reaches the flag, the game fades to black and the four characters dance to the victory music,
// like the "battle won" scene of Deltarune:
//   1. Kris, Susie, Ralsei and Noelle each dance alone on a purple spotlight, one after the other,
//   2. then all four dance in a circle,
//   3. then they line up and LEVEL WON! appears with a burst of music notes,
//   4. then the score and the PLAY AGAIN / EXIT buttons fade in under it, on the same screen (there is no pop-up window): the
//      characters keep dancing until the player chooses.
// Any key, mouse button or gamepad button skips to the last part. Enter / R = play again, M / Esc = exit (the main menu).
// It runs on real time (the game is stopped behind it).
//
// Everything is built in code on its own canvas. The pictures come from CelebrationArt: the dance frames of Kris, Susie and Ralsei
// (Assets/Characters/Dance), and Noelle's own dance made from her pictures in the game. With no art the screen with the score of
// LevelEnd is used instead (`fallback`).
public class WinCelebration : MonoBehaviour
{
    const string ArtResource = "CelebrationArt";
    const string SoundResource = "Audio/level_won";
    const string FontResource = "Fonts/LilitaOne SDF";

    // the timeline, in seconds
    const float Intro = 0.8f;                         // the screen goes black
    const float SoloStart = 0.9f, SoloLength = 2.1f;  // four solos
    const float CircleStart = 9.3f;                   // the circle dance
    const float RowStart = 13.3f, RowEnd = 14.2f;     // they line up
    const float TitleAt = 14.0f;                      // LEVEL WON!
    const float ResultsAt = 16.6f;                    // the score and the buttons come
    const float NoSkipBefore = 0.8f, KeysAfterResults = 0.7f;
    const float Beat = 0.46f;                         // the length of one dance beat

    static readonly Color32 Spotlight = new Color32(101, 96, 168, 255);
    static readonly Vector2 SoloSpot = new Vector2(0f, -150f);
    static readonly Vector2 CircleCentre = new Vector2(0f, -110f), CircleRadius = new Vector2(500f, 190f);
    const float RowY = -215f, RowSpacing = 320f, TitleY = 360f, ScoreY = 190f, ButtonsY = -415f;

    class Dancer
    {
        public RectTransform rt;
        public Image img;
        public CelebrationArt.DanceSet set;
        public float factor;                          // picture pixels -> screen units
        public float fps;                             // how fast the dance frames change
        public float phase;                           // so that they do not all hop at the same moment
        public float nextNote;
        public Vector2 pos;
    }

    class Note { public RectTransform rt; public Image img; public Vector2 start; public float born, life, sway, rise; }

    readonly List<Dancer> dancers = new List<Dancer>();
    readonly List<Note> notes = new List<Note>();
    RectTransform stage, noteLayer, spotlight, title;
    Image background;
    CanvasGroup content, results;
    TMP_Text scoreLabel;
    AudioSource sound;
    Sprite noteSprite;
    TMP_FontAsset font;
    Action playAgain, exit;
    int score;
    float age;
    bool titleShown, burstDone, skipped, musicBack;

    // Plays the celebration. `fallback` is called instead (at once) if the pictures are missing.
    public static void Play(int score, Action playAgain, Action exit, Action fallback)
    {
        var art = Resources.Load<CelebrationArt>(ArtResource);
        if (art == null || !art.Complete)
        {
            Debug.LogWarning("WinCelebration: no celebration art (Tools > Setup Celebration Art makes it), so there is no dance.");
            fallback?.Invoke();
            return;
        }
        var go = new GameObject("Win Celebration");
        var celebration = go.AddComponent<WinCelebration>();
        celebration.score = score;
        celebration.playAgain = playAgain;
        celebration.exit = exit;
        celebration.Build(art);
    }

    // ---- building ----

    void Build(CelebrationArt art)
    {
        font = Resources.Load<TMP_FontAsset>(FontResource);

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;                                     // above the HUD, the pause button and the pad
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        LevelEnd.EnsureEventSystem();                                 // the buttons need one

        var back = NewRect("Black", transform);
        Stretch(back);
        background = back.gameObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0f);                // fades to black (it also catches the clicks)

        var contentRect = NewRect("Content", transform);
        Stretch(contentRect);
        content = contentRect.gameObject.AddComponent<CanvasGroup>();
        content.alpha = 0f;

        spotlight = NewRect("Spotlight", contentRect);
        spotlight.sizeDelta = new Vector2(820f, 205f);
        var spotImage = spotlight.gameObject.AddComponent<Image>();
        spotImage.sprite = FountainFx.Ellipse;
        spotImage.color = Spotlight;
        spotImage.raycastTarget = false;
        spotlight.gameObject.SetActive(false);

        stage = NewRect("Stage", contentRect);
        Stretch(stage);
        // How big each one is drawn (the pictures are of different sizes: these factors make them about equally tall) and how fast
        // their dance frames change.
        AddDancer(art.kris, 7.4f, 6f, 0f);
        AddDancer(art.susie, 6.1f, 9f, 1f);
        AddDancer(art.ralsei, 6.4f, 7f, 2f);
        AddDancer(art.noelle, 2.1f, 10f, 3f);

        noteLayer = NewRect("Notes", contentRect);
        Stretch(noteLayer);
        noteSprite = MakeNote();

        // LEVEL WON! in the font of the rest of the game
        title = NewText("Level Won", contentRect, "LEVEL WON!", 170f, new Vector2(0f, TitleY), new Vector2(1500f, 210f), Color.white).rectTransform;
        title.gameObject.SetActive(false);

        BuildResults(contentRect);

        var clip = Resources.Load<AudioClip>(SoundResource);
        if (clip != null)
        {
            sound = gameObject.AddComponent<AudioSource>();
            sound.clip = clip;
            sound.spatialBlend = 0f;
            var audio = AudioManager.Instance;
            sound.volume = audio != null ? (audio.Muted ? 0f : audio.SfxVolume) : 0.8f;
            sound.Play();
            if (audio != null) audio.PauseMusic();                    // the game song waits for the victory music
        }
    }

    void AddDancer(CelebrationArt.DanceSet set, float factor, float fps, float phase)
    {
        var first = set.dance != null && set.dance.Length > 0 ? set.dance[0] : set.idle[0];
        var rect = NewRect("Dancer", stage);
        rect.pivot = new Vector2(0.5f, 0f);                           // his feet
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.sprite = first;
        rect.sizeDelta = first.rect.size * factor;
        rect.gameObject.SetActive(false);
        dancers.Add(new Dancer { rt = rect, img = image, set = set, factor = factor, fps = fps, phase = phase, nextNote = UnityEngine.Random.Range(0f, 0.4f) });
    }

    // The score and the two buttons, under LEVEL WON! (hidden until ResultsAt).
    void BuildResults(RectTransform parent)
    {
        var holder = NewRect("Results", parent);
        Stretch(holder);
        results = holder.gameObject.AddComponent<CanvasGroup>();
        results.alpha = 0f;
        results.interactable = false;
        results.blocksRaycasts = false;

        scoreLabel = NewText("Score", holder, "SCORE   0", 110f, new Vector2(0f, ScoreY), new Vector2(1100f, 140f), Color.white);
        MakeButton(holder, "PLAY AGAIN", new Vector2(-290f, ButtonsY), () => playAgain?.Invoke());
        MakeButton(holder, "EXIT", new Vector2(290f, ButtonsY), () => exit?.Invoke());
    }

    void MakeButton(RectTransform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var frame = NewRect(label + " frame", parent);
        frame.sizeDelta = new Vector2(520f, 120f);
        frame.anchoredPosition = position;
        frame.gameObject.AddComponent<Image>().color = Color.black;

        var faceRect = NewRect(label + " button", frame);
        faceRect.sizeDelta = new Vector2(508f, 108f);
        var face = faceRect.gameObject.AddComponent<Image>();
        face.color = new Color(1f, 0.85f, 0.35f);
        var button = faceRect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        var colours = button.colors;
        colours.highlightedColor = new Color(1f, 0.95f, 0.7f);
        colours.pressedColor = new Color(0.9f, 0.7f, 0.2f);
        button.colors = colours;
        button.onClick.AddListener(action);
        var text = NewText("Text", faceRect, label, 60f, Vector2.zero, new Vector2(500f, 100f), Color.black);
        text.raycastTarget = false;
    }

    TMP_Text NewText(string name, RectTransform parent, string text, float size, Vector2 position, Vector2 box, Color colour)
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
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // ---- time ----

    void Update()
    {
        age += Mathf.Min(Time.unscaledDeltaTime, 0.1f);                // a hitch while the scene is built must not skip a part

        if (!skipped && age >= NoSkipBefore && age < ResultsAt && SkipPressed())
        {
            skipped = true;                                            // straight to LEVEL WON! with the score and the buttons
            age = ResultsAt;
            if (sound != null) sound.Stop();
        }

        background.color = new Color(0f, 0f, 0f, Mathf.Clamp01(age / Intro));
        content.alpha = age < Intro ? 0f : 1f;

        if (age >= SoloStart) Dance();
        UpdateNotes();
        UpdateResults();

        if (!musicBack && (age >= ResultsAt || (sound != null && age > 2f && !sound.isPlaying)))
        {
            musicBack = true;                                          // the game song goes on once the victory music is over
            if (AudioManager.Instance != null) AudioManager.Instance.ResumeMusic();
        }
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

    void UpdateResults()
    {
        float k = Mathf.Clamp01((age - ResultsAt) / 0.5f);
        results.alpha = k;
        bool ready = age >= ResultsAt + KeysAfterResults;
        results.interactable = ready;
        results.blocksRaycasts = ready;
        if (age < ResultsAt) return;

        float count = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - ResultsAt) / 1.2f));          // the score counts up
        scoreLabel.text = "SCORE   " + Mathf.RoundToInt(score * count);

        if (!ready) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame) playAgain?.Invoke();
        else if (kb.mKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) exit?.Invoke();
    }

    void OnDestroy()
    {
        if (!musicBack && AudioManager.Instance != null) AudioManager.Instance.ResumeMusic();      // the scene went away
    }

    // ---- the dance ----

    void Dance()
    {
        float t = age - SoloStart;
        int soloIndex = Mathf.FloorToInt(t / SoloLength);

        if (age < CircleStart)
        {
            // one at a time, alone on the spotlight
            soloIndex = Mathf.Clamp(soloIndex, 0, dancers.Count - 1);
            spotlight.gameObject.SetActive(true);
            spotlight.anchoredPosition = SoloSpot + new Vector2(0f, -8f);
            for (int i = 0; i < dancers.Count; i++)
            {
                dancers[i].rt.gameObject.SetActive(i == soloIndex);
                if (i == soloIndex) Pose(dancers[i], age, SoloSpot);
            }
        }
        else
        {
            spotlight.gameObject.SetActive(false);
            float lineK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RowStart, RowEnd, age));
            for (int i = 0; i < dancers.Count; i++)
            {
                float angle = (Mathf.Min(age, RowStart) - CircleStart) * 0.8f + i * Mathf.PI * 0.5f;
                var orbit = CircleCentre + new Vector2(Mathf.Cos(angle) * CircleRadius.x, Mathf.Sin(angle) * CircleRadius.y);
                var row = new Vector2((i - (dancers.Count - 1) * 0.5f) * RowSpacing, RowY);
                dancers[i].rt.gameObject.SetActive(true);
                Pose(dancers[i], age, Vector2.Lerp(orbit, row, lineK));
            }
            SortByDepth();
        }

        if (age >= TitleAt && !titleShown) { titleShown = true; title.gameObject.SetActive(true); }
        if (titleShown)
        {
            float k = Mathf.Clamp01((age - TitleAt) / 0.4f);
            float pop = k < 1f ? 1f + Mathf.Sin(k * Mathf.PI) * 0.35f : 1f;         // a little overshoot
            title.localScale = Vector3.one * (k * pop);
            if (!burstDone && k >= 0.5f)
            {
                burstDone = true;
                for (int i = 0; i < 16; i++)                                          // the burst of notes
                {
                    var at = title.anchoredPosition + new Vector2(UnityEngine.Random.Range(-380f, 380f), UnityEngine.Random.Range(-30f, 60f));
                    SpawnNote(at, UnityEngine.Random.Range(0.9f, 1.8f), UnityEngine.Random.Range(160f, 320f));
                }
            }
        }

        foreach (var d in dancers)                                                    // each one lets a note float up now and then
        {
            if (!d.rt.gameObject.activeSelf || age < d.nextNote) continue;
            d.nextNote = age + UnityEngine.Random.Range(0.45f, 0.8f);
            SpawnNote(d.rt.anchoredPosition + new Vector2(UnityEngine.Random.Range(-40f, 40f), d.rt.sizeDelta.y * 0.9f), 1.5f, 110f);
        }
    }

    // Kris, Susie and Ralsei: their dance frames in a loop, with a small bob on every beat.
    // Noelle has no dance frames of her own, so hers is made from her pictures (see below).
    void Pose(Dancer d, float t, Vector2 pos)
    {
        float b = t / Beat + d.phase;
        d.pos = pos;

        if (d.set.dance != null && d.set.dance.Length > 0)
        {
            var frames = d.set.dance;
            var frame = frames[(int)(t * d.fps + d.phase * 3f) % frames.Length];
            d.img.sprite = frame;
            d.rt.sizeDelta = frame.rect.size * d.factor;
            d.rt.anchoredPosition = pos + new Vector2(0f, Mathf.Abs(Mathf.Sin(b * Mathf.PI)) * 14f);
            d.rt.localRotation = Quaternion.identity;
            d.rt.localScale = Vector3.one;
            return;
        }

        // Noelle does not hop: her arms swing (the spell pictures) while she slides from side to side, facing the way she slides,
        // with a slight lean.
        float sway = Mathf.Sin(b * Mathf.PI * 0.5f);                 // slides right and left over four beats
        float dir = Mathf.Cos(b * Mathf.PI * 0.5f);                  // the way she slides
        var cycle = d.set.special != null && d.set.special.Length > 0 ? d.set.special : d.set.move;
        var s = cycle[(int)(t * d.fps + d.phase * 3f) % cycle.Length];

        d.img.sprite = s;
        d.rt.sizeDelta = s.rect.size * d.factor;
        d.rt.anchoredPosition = pos + new Vector2(sway * 26f, 0f);
        d.rt.localRotation = Quaternion.Euler(0f, 0f, sway * 4f);
        d.rt.localScale = new Vector3(dir >= 0f ? 1f : -1f, 1f, 1f);
    }

    // Lower on the screen = nearer = drawn in front.
    void SortByDepth()
    {
        var order = new List<Dancer>(dancers);
        order.Sort((a, b) => b.pos.y.CompareTo(a.pos.y));
        for (int i = 0; i < order.Count; i++) order[i].rt.SetSiblingIndex(i);
    }

    // ---- the notes ----

    void SpawnNote(Vector2 at, float life, float rise)
    {
        var rect = NewRect("Note", noteLayer);
        rect.sizeDelta = new Vector2(noteSprite.rect.width, noteSprite.rect.height) * 5f;
        rect.anchoredPosition = at;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = noteSprite;
        image.raycastTarget = false;
        image.color = UnityEngine.Random.value < 0.5f ? Color.white : new Color(0.7f, 0.7f, 0.74f);
        notes.Add(new Note { rt = rect, img = image, start = at, born = age, life = life, sway = UnityEngine.Random.Range(-1f, 1f), rise = rise });
    }

    void UpdateNotes()
    {
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var n = notes[i];
            float k = (age - n.born) / n.life;
            if (k >= 1f) { Destroy(n.rt.gameObject); notes.RemoveAt(i); continue; }
            n.rt.anchoredPosition = n.start + new Vector2(Mathf.Sin(k * 6f + n.sway * 3f) * 22f * n.sway, k * n.rise);
            var c = n.img.color;
            c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            n.img.color = c;
        }
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

    // ---- pictures drawn in code ----

    // A pixel art eighth note, white.
    static Sprite MakeNote()
    {
        string[] rows =
        {
            "...####.",
            "...#..##",
            "...#...#",
            "...#....",
            "...#....",
            "...#....",
            "...#....",
            ".###....",
            "####....",
            "####....",
            ".##.....",
        };
        return FromRows(rows);
    }

    static Sprite FromRows(string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, rows[y][x] == '#' ? Color.white : Color.clear);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
    }
}
