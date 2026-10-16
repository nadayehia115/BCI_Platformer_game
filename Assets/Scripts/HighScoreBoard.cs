using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The "HIGH SCORES" panel on the main menu: the top 3 scores (HighScores), on the right of the buttons.
// It is built in code every time the main menu loads, as a child of the menu's MainPanel (so it fades away with the buttons
// when How To Play is open), which means it is always up to date and the menu scene does not have to be rebuilt for it.
public class HighScoreBoard : MonoBehaviour
{
    const string MenuSceneName = "MainMenu";
    const string FontResource = "Fonts/LilitaOne SDF";

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

        var parent = GameObject.Find("MainPanel");                         // the menu's buttons live here
        if (parent == null) parent = GameObject.Find("MenuCanvas");
        if (parent == null || parent.transform.Find("High Scores") != null) return;
        Build(parent.transform);
    }

    static void Build(Transform parent)
    {
        var font = Resources.Load<TMP_FontAsset>(FontResource);

        var frame = NewImage("High Scores", parent, new Vector2(470f, 470f), Color.black);
        var rect = frame.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);          // right side, middle
        rect.anchoredPosition = new Vector2(-70f, -70f);
        var panel = NewImage("Panel", frame.transform, new Vector2(450f, 450f), new Color(0.97f, 0.95f, 0.88f));

        NewText("Title", panel.transform, "HIGH SCORES", 58f, new Vector2(0f, 170f), new Vector2(430f, 80f), font, Color.black);

        var scores = HighScores.Top();
        string[] medals = { "1ST", "2ND", "3RD" };
        Color[] colours = { new Color(0.8f, 0.55f, 0.0f), new Color(0.35f, 0.4f, 0.45f), new Color(0.6f, 0.3f, 0.1f) };
        for (int i = 0; i < HighScores.Count; i++)
        {
            float y = 60f - i * 110f;
            var row = NewImage("Row " + (i + 1), panel.transform, new Vector2(400f, 92f), new Color(1f, 1f, 1f, 0.55f));
            row.rectTransform.anchoredPosition = new Vector2(0f, y);
            NewText("Place", row.transform, medals[i], 44f, new Vector2(-120f, 0f), new Vector2(130f, 80f), font, colours[i]);
            string value = scores[i] > 0 ? scores[i].ToString() : "---";
            NewText("Score", row.transform, value, 62f, new Vector2(70f, 0f), new Vector2(240f, 90f), font, scores[i] > 0 ? Color.black : new Color(0.45f, 0.45f, 0.45f));
        }
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

    static void NewText(string name, Transform parent, string text, float size, Vector2 position, Vector2 box, TMP_FontAsset font, Color colour)
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
        t.color = colour;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }
}
