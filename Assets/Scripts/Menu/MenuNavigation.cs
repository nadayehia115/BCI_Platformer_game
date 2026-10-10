using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Moves between the main panel and the two How to Play pages with fades, loads the game, quits.
public class MenuNavigation : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] CanvasGroup mainPanel;
    [SerializeField] CanvasGroup howToPage1;
    [SerializeField] CanvasGroup howToPage2;

    [Header("Navigation")]
    [SerializeField] string gameSceneName = "GameScene";
    [SerializeField] float fadeTime = 0.25f;

    [Header("Keyboard focus")]
    [SerializeField] GameObject firstMainButton;
    [SerializeField] GameObject page1FirstButton;
    [SerializeField] GameObject page2FirstButton;

    CanvasGroup current;
    Coroutine running;
    bool busy;

    void Start()
    {
        Time.timeScale = 1f;
        SetGroup(howToPage1, 0f);
        SetGroup(howToPage2, 0f);
        SetGroup(mainPanel, 1f);
        current = mainPanel;
        Select(firstMainButton);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && current != mainPanel) ExitToMenu();
    }

    // ---- button actions (wired to the buttons' OnClick events) -----------------------------
    public void Play()
    {
        if (busy) return;
        StartCoroutine(PlayRoutine());
    }

    public void ShowHowToPlay() => Switch(howToPage1, page1FirstButton);
    public void NextPage() => Switch(howToPage2, page2FirstButton);
    public void PreviousPage() => Switch(howToPage1, page1FirstButton);
    public void ExitToMenu() => Switch(mainPanel, firstMainButton);

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- internals -----------------------------------------------------------------------
    IEnumerator PlayRoutine()
    {
        busy = true;
        yield return Fade(current, 1f, 0f);
        SceneManager.LoadScene(gameSceneName);
    }

    void Switch(CanvasGroup next, GameObject focus)
    {
        if (busy || next == current) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(SwitchRoutine(next, focus));
    }

    IEnumerator SwitchRoutine(CanvasGroup next, GameObject focus)
    {
        busy = true;
        yield return Fade(current, 1f, 0f);
        current = next;
        yield return Fade(next, 0f, 1f);
        Select(focus);
        busy = false;
    }

    IEnumerator Fade(CanvasGroup group, float from, float to)
    {
        group.interactable = group.blocksRaycasts = false;
        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / fadeTime));
            yield return null;
        }
        SetGroup(group, to);
    }

    static void SetGroup(CanvasGroup g, float alpha)
    {
        g.alpha = alpha;
        g.interactable = g.blocksRaycasts = alpha > 0.5f;
    }

    static void Select(GameObject go)
    {
        if (go != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go);
    }
}
