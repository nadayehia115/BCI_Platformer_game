using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// On-screen score ("SCORE 3 / 15", top left) in GameScene. It builds its own canvas when the scene starts, so it lives in
// code instead of in the scene file and survives merges. It listens to ScoreManager.Changed (fed by every ScorePoint gem).
public class ScoreUI : MonoBehaviour
{
    const string SceneName = "GameScene";
    const string FontResource = "Fonts/LilitaOne SDF";       // Assets/Resources/Fonts; falls back to TextMeshPro's default font

    TMP_Text label;
    int total;
    float pop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (SceneManager.GetActiveScene().name != SceneName) return;
        if (FindFirstObjectByType<ScoreUI>() != null) return;
        new GameObject("Score UI").AddComponent<ScoreUI>();
    }

    void Awake()
    {
        total = FindObjectsByType<ScorePoint>(FindObjectsSortMode.None).Length;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var go = new GameObject("Score Text", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(48f, -36f);
        rect.sizeDelta = new Vector2(700f, 90f);

        label = go.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>(FontResource);
        if (font != null) label.font = font;
        label.fontSize = 64f;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.color = Color.black;
        label.outlineWidth = 0.25f;                           // white edge so it reads on both the sky and the ground
        label.outlineColor = Color.white;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
    }

    void OnEnable()
    {
        ScoreManager.Changed += OnScore;
        Show(ScoreManager.Score);
    }

    void OnDisable() => ScoreManager.Changed -= OnScore;

    void OnScore(int score)
    {
        Show(score);
        pop = 1f;
    }

    void Show(int score) => label.text = total > 0 ? $"SCORE  {score} / {total}" : $"SCORE  {score}";

    // a small pop each time a gem is collected
    void Update()
    {
        if (pop <= 0f) return;
        pop = Mathf.Max(0f, pop - Time.unscaledDeltaTime * 4f);
        label.rectTransform.localScale = Vector3.one * (1f + 0.25f * pop);
    }
}
