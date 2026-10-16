using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Kris has 3 lives. Falling into water or touching spikes costs one: he is put back on the last safe ground he stood on
// (about a second before), and blinks for a moment during which nothing can hurt him. When the last life is gone the game
// ends with the YOU LOSE screen (LevelEnd).
// Spikes (an object named like Spike...) and anything with a Hazard component cost a life as soon as he touches them. Water
// only does when his WHOLE body is under it (all four corners of his collider are inside water tiles), so wading in at the
// edge or splashing through the top is fine. It builds itself when GameScene loads (like LevelEnd), so nothing has to be placed in the scene.
public class LivesManager : MonoBehaviour
{
    const string GameSceneName = "GameScene";
    public const int StartLives = 3;

    [SerializeField] float invulnerableSeconds = 1.5f;
    [Tooltip("How solid the water looks (1 = opaque). Kris is drawn behind it, so lower shows him more clearly when he is under.")]
    [SerializeField, Range(0.3f, 1f)] float waterOpacity = 0.75f;
    [Tooltip("Respawn at the place Kris stood on solid ground this many seconds before he was hurt.")]
    [SerializeField] float respawnLookBack = 1f;

    public static LivesManager Instance { get; private set; }
    public int Lives { get; private set; } = StartLives;
    public bool Invulnerable => Time.time < invulnerableUntil;

    PlayerController kris;
    Rigidbody2D body;
    Collider2D col;
    SpriteRenderer krisSprite;
    Tilemap water;
    float invulnerableUntil;
    float nextRecord;
    Vector2 startPosition;
    readonly List<(float time, Vector2 position)> safeSpots = new List<(float, Vector2)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != GameSceneName || FindFirstObjectByType<LivesManager>() != null) return;
        new GameObject("Lives").AddComponent<LivesManager>();
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        kris = FindFirstObjectByType<PlayerController>();
        if (kris == null)
        {
            Debug.LogWarning("LivesManager: no PlayerController in the scene, so there are no lives to lose.");
            enabled = false;
            return;
        }
        body = kris.GetComponent<Rigidbody2D>();
        col = kris.GetComponent<Collider2D>();
        krisSprite = kris.GetComponent<SpriteRenderer>();
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (tm.name == "Tilemap_Water") water = tm;
        startPosition = kris.transform.position;
        PutWaterInFrontOfKris();
        kris.gameObject.AddComponent<HazardSensor>().owner = this;      // hears the trigger zones he touches
    }

    // Kris was drawn on top of the water, so he looked like he was floating over it. The water is drawn in front of him instead,
    // a little see-through, so when he sinks in he is really behind the water. (The ice stays behind him: see NoelleFreeze.)
    void PutWaterInFrontOfKris()
    {
        if (water == null || krisSprite == null) return;
        var waterRenderer = water.GetComponent<TilemapRenderer>();
        if (waterRenderer != null)
        {
            waterRenderer.sortingLayerID = krisSprite.sortingLayerID;
            waterRenderer.sortingOrder = krisSprite.sortingOrder + 1;
        }
        var tint = water.color;
        tint.a = Mathf.Min(tint.a, waterOpacity);
        water.color = tint;
    }

    // ---- remembering where it was safe ----

    void Update()
    {
        if (kris == null || Time.time < nextRecord) return;
        nextRecord = Time.time + 0.1f;

        bool standing = body != null && Mathf.Abs(body.linearVelocity.y) < 0.05f && OnGround();
        if (standing && !Invulnerable)
        {
            safeSpots.Add((Time.time, (Vector2)kris.transform.position));
            safeSpots.RemoveAll(s => Time.time - s.time > 6f);
        }
    }

    // Solid ground right under his feet (the Ground layer is what the player's own ground check uses too).
    bool OnGround()
    {
        if (col == null) return false;
        var b = col.bounds;
        int mask = LayerMask.GetMask("Ground");
        return Physics2D.BoxCast(b.center, new Vector2(b.size.x * 0.8f, b.size.y), 0f, Vector2.down, 0.1f, mask).collider != null;
    }

    // ---- getting hurt ----

    // Spikes and anything marked with a Hazard component: a life is lost on touch.
    public static bool IsHazard(Collider2D c)
    {
        if (c.GetComponentInParent<Hazard>() != null) return true;
        for (var t = c.transform; t != null; t = t.parent)
            if (t.name.StartsWith("Spike")) return true;
        return false;
    }

    public static bool IsWater(Collider2D c) => c.name == "Tilemap_Water";

    // Is his whole body under the water? All four corners of his collider (a hair inside) have to be in water tiles.
    bool Submerged()
    {
        if (water == null || col == null) return false;
        var b = col.bounds;
        const float inset = 0.05f;
        var corners = new[]
        {
            new Vector3(b.min.x + inset, b.min.y + inset, 0f), new Vector3(b.max.x - inset, b.min.y + inset, 0f),
            new Vector3(b.min.x + inset, b.max.y - inset, 0f), new Vector3(b.max.x - inset, b.max.y - inset, 0f),
        };
        foreach (var corner in corners)
            if (!water.HasTile(water.WorldToCell(corner))) return false;
        return true;
    }

    void Hurt()
    {
        if (Invulnerable || Lives <= 0 || (LevelEnd.Instance != null && LevelEnd.Instance.Ended)) return;

        Lives--;
        invulnerableUntil = Time.time + invulnerableSeconds;
        if (Lives <= 0)
        {
            if (LevelEnd.Instance != null) LevelEnd.Instance.Lose("You ran out of lives.");
            return;
        }
        Respawn();
        StartCoroutine(Blink());
    }

    void Respawn()
    {
        Vector2 target = startPosition;
        for (int i = safeSpots.Count - 1; i >= 0; i--)                       // the latest safe spot that is old enough
        {
            if (Time.time - safeSpots[i].time >= respawnLookBack)
            {
                target = safeSpots[i].position;
                break;
            }
        }
        if (target == startPosition && safeSpots.Count > 0) target = safeSpots[0].position;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.position = target;
        }
        kris.transform.position = target;
        RemoteInput.SetHorizontal(0f);
    }

    IEnumerator Blink()
    {
        while (Invulnerable)
        {
            SetKrisAlpha(Mathf.Sin(Time.time * 40f) > 0f ? 1f : 0.3f);
            yield return null;
        }
        SetKrisAlpha(1f);
    }

    void SetKrisAlpha(float a)
    {
        if (krisSprite == null) return;
        var c = krisSprite.color;
        c.a = a;
        krisSprite.color = c;
    }

    // Lives for a new run (play again reloads the scene, which builds a new manager, so this is only for completeness).
    public void ResetLives() => Lives = StartLives;

    // Sits on Kris and tells the manager when he touches a hazard. (Trigger events go to the object that has the rigidbody.)
    class HazardSensor : MonoBehaviour
    {
        public LivesManager owner;

        void OnTriggerEnter2D(Collider2D other) => Check(other);
        void OnTriggerStay2D(Collider2D other) => Check(other);     // still standing in it when the blinking is over

        void Check(Collider2D other)
        {
            if (IsHazard(other)) owner.Hurt();
            else if (IsWater(other) && owner.Submerged()) owner.Hurt();       // water: only when he is completely under
        }
    }
}
