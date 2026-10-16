using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The HUD at the top left of GameScene: the time left ("TIME 3:59", turns red in the last 30 seconds) and three hearts for
// Kris's lives (a heart greys out for every life lost). There is no running score: the score is worked out at the end, from
// the time left (see LevelEnd). It builds its own canvas whenever GameScene loads (from the main menu, directly, or again
// after play again), so it lives in code instead of in the scene file and survives merges.
public class ScoreUI : MonoBehaviour
{
    const string SceneName = "GameScene";
    const string FontResource = "Fonts/LilitaOne SDF";       // Assets/Resources/Fonts; falls back to TextMeshPro's default font

    TMP_Text timeLabel;
    float lastBonus, bonusFlash;
    Image[] hearts;
    Sprite heartFull, heartEmpty;
    int shownLives = -1;
    int popHeart = -1;
    float heartPop;

    // A published game starts at the main menu, so this has to run for every scene that loads, not only the first one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneName || FindFirstObjectByType<ScoreUI>() != null) return;
        new GameObject("Score UI").AddComponent<ScoreUI>();
    }

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var font = Resources.Load<TMP_FontAsset>(FontResource);

        var timeGo = new GameObject("Time Text", typeof(RectTransform));
        timeGo.transform.SetParent(transform, false);
        var timeRect = (RectTransform)timeGo.transform;
        timeRect.anchorMin = timeRect.anchorMax = timeRect.pivot = new Vector2(0f, 1f);
        timeRect.anchoredPosition = new Vector2(48f, -36f);
        timeRect.sizeDelta = new Vector2(700f, 90f);
        timeLabel = StyleText(timeGo.AddComponent<TextMeshProUGUI>(), font);

        heartFull = HeartSprite(true);
        heartEmpty = HeartSprite(false);
        hearts = new Image[LivesManager.StartLives];
        for (int i = 0; i < hearts.Length; i++)
        {
            var heart = new GameObject("Heart " + (i + 1), typeof(RectTransform));
            heart.transform.SetParent(transform, false);
            var heartRect = (RectTransform)heart.transform;
            heartRect.anchorMin = heartRect.anchorMax = heartRect.pivot = new Vector2(0f, 1f);
            heartRect.anchoredPosition = new Vector2(48f + i * 88f, -120f);
            heartRect.sizeDelta = new Vector2(77f, 70f);
            var image = heart.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            hearts[i] = image;
        }
    }

    // A pixel art heart, 11 x 10: black outline, red (or grey, when the life is gone) fill and a small shine.
    static Sprite HeartSprite(bool full)
    {
        string[] rows =
        {
            "..###.###..",
            ".#rrr#rrr#.",
            "#rhrrrrrrr#",
            "#rhrrrrrrr#",
            "#rrrrrrrrr#",
            ".#rrrrrrr#.",
            "..#rrrrr#..",
            "...#rrr#...",
            "....#r#....",
            ".....#.....",
        };
        var red = full ? new Color(0.9f, 0.1f, 0.15f) : new Color(0.55f, 0.55f, 0.58f);
        var shine = full ? new Color(1f, 0.65f, 0.7f) : new Color(0.75f, 0.75f, 0.78f);
        int w = rows[0].Length, h = rows.Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = rows[y][x];
                tex.SetPixel(x, h - 1 - y, c == '#' ? Color.black : c == 'r' ? red : c == 'h' ? shine : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    static TMP_Text StyleText(TMP_Text t, TMP_FontAsset font)
    {
        if (font != null) t.font = font;
        t.fontSize = 64f;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.color = Color.black;
        t.outlineWidth = 0.25f;                               // white edge so it reads on both the sky and the ground
        t.outlineColor = Color.white;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // Hearts: full for the lives left, grey for the lives lost; the heart that was just lost pops.
    void ShowLives()
    {
        int lives = LivesManager.Instance != null ? LivesManager.Instance.Lives : LivesManager.StartLives;
        if (lives != shownLives)
        {
            if (shownLives > lives && lives >= 0 && lives < hearts.Length) { popHeart = lives; heartPop = 1f; }
            shownLives = lives;
            for (int i = 0; i < hearts.Length; i++) hearts[i].sprite = i < lives ? heartFull : heartEmpty;
        }
        if (heartPop > 0f && popHeart >= 0)
        {
            heartPop = Mathf.Max(0f, heartPop - Time.unscaledDeltaTime * 3f);
            hearts[popHeart].rectTransform.localScale = Vector3.one * (1f + 0.7f * heartPop);
        }
    }

    // the time left (counts down; red in the last 30 seconds and once it has run out; flashes green when a gem adds time)
    void Update()
    {
        var level = LevelEnd.Instance;
        if (level != null)
        {
            timeLabel.text = "TIME  " + LevelEnd.FormatTime(level.Remaining, tenthsWhenLow: true);
            if (level.Bonus > lastBonus) bonusFlash = 1f;                       // a gem just added time: flash the clock green
            lastBonus = level.Bonus;
            bonusFlash = Mathf.Max(0f, bonusFlash - Time.unscaledDeltaTime * 3f);
            var normal = level.Remaining <= 30f ? new Color(0.8f, 0.05f, 0.05f) : Color.black;
            timeLabel.color = Color.Lerp(normal, new Color(0.05f, 0.55f, 0.15f), bonusFlash);
        }

        ShowLives();
    }
}
