using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// The end of the level. A clock runs from the moment GameScene starts; when Kris reaches the flag the game stops and a
// screen says YOU WIN (he got there within the time limit) or YOU LOSE (he was too slow), with the time and the score and
// buttons to play again or go back to the main menu (Enter / R and M / Esc work too).
// It builds itself when GameScene loads, in code, so nothing has to be placed in the scene and a merge cannot drop it.
public class LevelEnd : MonoBehaviour
{
    const string GameSceneName = "GameScene", MenuSceneName = "MainMenu";
    const string FontResource = "Fonts/LilitaOne SDF";

    [Tooltip("Seconds Kris has to reach the flag to win.")]
    [SerializeField] float timeLimit = 240f;

    public static LevelEnd Instance { get; private set; }

    public float Elapsed { get; private set; }
    public float Bonus { get; private set; }                    // seconds added by collected gems
    public float TimeLimit => timeLimit + Bonus;                // the limit including the bonus
    public float Remaining => Mathf.Max(0f, TimeLimit - Elapsed);
    public bool Ended { get; private set; }

    GameObject screen;

    // GameScene can be reached from the main menu or started directly, and can be loaded again (play again), so this
    // listens for every scene load instead of running once.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != GameSceneName || FindFirstObjectByType<LevelEnd>() != null) return;
        new GameObject("Level End").AddComponent<LevelEnd>();
    }

    // m:ss; with tenths (m:ss.t) in the last ten seconds, where a gem's 0.1 s bonus can be seen.
    public static string FormatTime(float seconds, bool tenthsWhenLow = false)
    {
        seconds = Mathf.Max(0f, seconds);
        if (tenthsWhenLow && seconds < 10f)
        {
            int tenths = Mathf.CeilToInt(seconds * 10f);
            return $"0:{tenths / 10:00}.{tenths % 10}";
        }
        int total = Mathf.CeilToInt(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    // Kris collected a gem: that many more seconds on the clock.
    public void AddTime(float seconds)
    {
        if (!Ended) Bonus += seconds;
    }

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
        ScoreManager.ResetScore();                       // a new run starts from zero
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    void Start()
    {
        var goal = FindFirstObjectByType<LevelGoal>();
        if (goal != null) timeLimit = goal.timeLimit;     // set on the Level Goal object in the Inspector
        CreateGoalZone();
    }

    void Update()
    {
        if (!Ended)
        {
            Elapsed += Time.deltaTime;                   // stops by itself when the game is stopped (timeScale 0)
            if (Elapsed >= TimeLimit) EndGame(false, "Time ran out before you reached the flag.", timeRanOut: true);    // the clock hit zero: the game stops, you lose
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame) PlayAgain();
        else if (kb.mKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) MainMenu();
    }

    // ---- the flag ----

    // A trigger zone at the flag: a LevelGoal object if the scene has one, otherwise the flag tile at the right-most end of
    // Tilemap_Decorations (the last decoration of the level).
    void CreateGoalZone()
    {
        Vector3 centre;
        var goal = FindFirstObjectByType<LevelGoal>();
        if (goal != null)
        {
            centre = goal.transform.position;
            if (goal.GetComponent<Collider2D>() != null) { AddRelay(goal.gameObject, null); return; }
        }
        else
        {
            Tilemap decorations = null;
            foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
                if (tm.name == "Tilemap_Decorations") decorations = tm;
            if (decorations == null) { Debug.LogWarning("LevelEnd: no LevelGoal and no Tilemap_Decorations, so there is no flag to reach."); return; }

            bool found = false;
            Vector3Int flag = default;
            foreach (var cell in decorations.cellBounds.allPositionsWithin)
                if (decorations.HasTile(cell) && (!found || cell.x > flag.x)) { flag = cell; found = true; }
            if (!found) { Debug.LogWarning("LevelEnd: Tilemap_Decorations is empty, so there is no flag to reach."); return; }
            centre = decorations.GetCellCenterWorld(flag);
        }

        var zone = new GameObject("Level Goal Zone");
        zone.transform.position = new Vector3(centre.x, centre.y, 0f);
        AddRelay(zone, new Vector2(3f, 4f));
    }

    void AddRelay(GameObject go, Vector2? size)
    {
        if (size.HasValue)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size.Value;
            box.isTrigger = true;
        }
        else
        {
            foreach (var c in go.GetComponents<Collider2D>()) c.isTrigger = true;
        }
        go.AddComponent<GoalRelay>().owner = this;
    }

    // Tells LevelEnd when the player touches the goal zone.
    class GoalRelay : MonoBehaviour
    {
        public LevelEnd owner;

        void OnTriggerEnter2D(Collider2D other)
        {
            var body = other.attachedRigidbody;
            if (body != null && body.GetComponent<PlayerController>() != null) owner.Reached();
        }
    }

    // ---- the end screen ----

    // Kris touched the flag (it can only be reached before the clock runs out, because the game stops at zero)
    void Reached()
    {
        bool win = Elapsed <= TimeLimit;
        EndGame(win, win ? "You reached the flag in time!" : "You reached the flag too late.");
    }

    // Anything else that ends the game as a loss (the lives ran out) calls this.
    public void Lose(string reason) => EndGame(false, reason);

    void EndGame(bool win, string message, bool timeRanOut = false)
    {
        if (Ended) return;
        Ended = true;
        if (timeRanOut) Elapsed = TimeLimit;
        Time.timeScale = 0f;                             // the game stops behind the screen
        RemoteInput.SetHorizontal(0f);
        BuildScreen(win, message);
    }

    void BuildScreen(bool win, string message)
    {
        screen = new GameObject("End Screen");
        screen.transform.SetParent(transform, false);
        var canvas = screen.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = screen.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        screen.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var font = Resources.Load<TMP_FontAsset>(FontResource);

        Image Box(string name, Transform parent, Vector2 size, Color colour, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.AddComponent<Image>();
            image.color = colour;
            return image;
        }

        TMP_Text Label(string content, Transform parent, float size, Vector2 position, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = box;
            rect.anchoredPosition = position;
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.black;
            t.raycastTarget = false;
            return t;
        }

        void MakeButton(string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var frame = Box(label + " frame", screen.transform, new Vector2(520f, 120f), Color.black, position);
            var face = Box(label + " button", frame.transform, new Vector2(508f, 108f), new Color(1f, 0.85f, 0.35f), Vector2.zero);
            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.highlightedColor = new Color(1f, 0.95f, 0.7f);
            colours.pressedColor = new Color(0.9f, 0.7f, 0.2f);
            button.colors = colours;
            button.onClick.AddListener(action);
            Label(label, face.transform, 60f, Vector2.zero, new Vector2(500f, 100f));
        }

        // dim the game, then a light panel with a black frame (black text on it, like the rest of the menus)
        var dim = Box("Dim", screen.transform, new Vector2(4000f, 3000f), new Color(0f, 0f, 0.05f, 0.7f), Vector2.zero);
        dim.raycastTarget = true;
        var frameOuter = Box("Panel frame", screen.transform, new Vector2(1200f, 780f), Color.black, new Vector2(0f, 20f));
        var panel = Box("Panel", frameOuter.transform, new Vector2(1180f, 760f), new Color(0.97f, 0.95f, 0.88f), Vector2.zero);

        var title = Label(win ? "YOU WIN!" : "YOU LOSE", panel.transform, 150f, new Vector2(0f, 250f), new Vector2(1100f, 190f));
        title.color = win ? new Color(0.05f, 0.45f, 0.2f) : new Color(0.65f, 0.1f, 0.1f);
        Label(message, panel.transform, 56f, new Vector2(0f, 130f), new Vector2(1100f, 80f));
        Label($"TIME   {FormatTime(Elapsed)}   (limit {FormatTime(TimeLimit)})", panel.transform, 64f, new Vector2(0f, 30f), new Vector2(1100f, 90f));
        Label($"SCORE   {ScoreManager.Score} / {FindTotalGems()}", panel.transform, 64f, new Vector2(0f, -60f), new Vector2(1100f, 90f));

        MakeButton("PLAY AGAIN", new Vector2(-300f, -125f), PlayAgain);
        MakeButton("MAIN MENU", new Vector2(300f, -125f), MainMenu);
        Label("Enter / R: play again      M / Esc: main menu", panel.transform, 40f, new Vector2(0f, -310f), new Vector2(1100f, 60f));
    }

    // The gems still in the level plus the ones already collected, so the screen can say "score / total".
    int FindTotalGems() => ScoreManager.Score + FindObjectsByType<ScorePoint>(FindObjectsSortMode.None).Length;

    // The buttons need an EventSystem; the project uses the new Input System, so its UI module is used.
    static void EnsureEventSystem()
    {
        var existing = FindFirstObjectByType<EventSystem>();
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                foreach (var old in existing.GetComponents<BaseInputModule>()) Destroy(old);     // the old module would throw with the new Input System
                existing.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            return;
        }
        var go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();            // clicks, the mouse and the gamepad
    }

    // ---- what the buttons do ----

    void PlayAgain()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void MainMenu()
    {
        Time.timeScale = 1f;
        if (Application.CanStreamedLevelBeLoaded(MenuSceneName)) SceneManager.LoadScene(MenuSceneName);
        else PlayAgain();                                // the menu is not in the build (testing in the editor)
    }
}
