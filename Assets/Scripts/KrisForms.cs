using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// The dark fountain. Kris wears his BLUE suit from the start and for the whole game. At every obstacle that needs a friend's power
// (a pool, a tree, a box) the player presses S (the Python S key, or S in Unity) and the DARK FOUNTAIN plays where Kris stands: the
// room goes dark, stars twinkle, Kris raises the knife and stabs the floor, a white flash, a beam of darkness, then black smoke
// rises to the top of the screen, spreads along it and comes down like a curtain until the whole screen is black.
//
// On that black screen the flickering SSVEP pad appears (SsvepBoard) and stays until the Python T (the classification says the
// player kept the focus). Then the pad goes away, the smoke lifts and shows the game again, and the friend the obstacle needs is
// brought in (CharacterSwitcher.AutoSelect). Kris and the level clock wait all that time. The next obstacle starts with S again.
//
// It builds itself when GameScene loads (like LevelEnd), so it survives merges. The sprites come from FountainArt.
// If the art is missing, S arms the pad at once, without the animation and without the black screen.
public class KrisForms : MonoBehaviour
{
    const string GameSceneName = "GameScene";

    public static KrisForms Instance { get; private set; }
    // "Armed": the fountain has played at the obstacle in front of Kris, the pad is out and T may activate the power. False from the
    // start, and again after a power was activated (the next obstacle needs S and the fountain again). Read by CharacterSwitcher
    // and SsvepBoard.
    public static bool PowersUnlocked { get; private set; } = true;

    // The fountain at the obstacle in front of Kris: the Python S key (message "s") and the S key in Unity. Nothing happens while
    // it is already playing, while the pad is already out, or when no obstacle needs a friend's power on the screen.
    public static void Request()
    {
        if (PauseMenu.IsPaused) return;
        if (Instance != null) Instance.RequestFountain();
    }

    // A power was activated: the pad is used up.
    public static void Disarm() => PowersUnlocked = false;
    public bool DarkWorld { get; private set; }

    // The smoke covers the whole screen and the pad is out on it: the game waits for T. Lift() takes the smoke away again and
    // then calls `whenDone` (CharacterSwitcher brings the friend in there, so that he is seen arriving).
    public bool Covered { get; private set; }

    // The hearts (ScoreUI) are hidden from the moment the smoke comes down until it has lifted again, like the portraits of the
    // friends: only the clock and the pause button stay on the black screen.
    public static bool LivesHidden { get; private set; }
    Action afterLift;
    bool lifting;

    public void Lift(Action whenDone)
    {
        if (!Covered || lifting) return;
        afterLift = whenDone;
        lifting = true;
    }

    FountainArt art;
    PlayerController kris;
    Rigidbody2D body;
    Collider2D col;
    SpriteRenderer krisSprite;
    Animator animator;
    CharacterSwitcher switcher;
    CanvasGroup hud;

    float feetOffset;                              // where Kris's feet are, relative to his position (measured from his picture)
    bool cutscene;

    // the sequence
    readonly List<GameObject> stage = new List<GameObject>();
    SpriteRenderer cut, overlay;
    Camera cam;
    Vector3 spot;
    float feetY;
    int layer, order;
    bool sparkling;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PowersUnlocked = true;
        LivesHidden = false;
        if (scene.name != GameSceneName || FindFirstObjectByType<KrisForms>() != null) return;
        new GameObject("Kris Forms").AddComponent<KrisForms>();
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PowersUnlocked = true;
        LivesHidden = false;
        if (LevelEnd.Instance != null) LevelEnd.Instance.Paused = false;
    }

    // ---- start: the powers are locked until the fountain ----

    void Start()
    {
        kris = FindFirstObjectByType<PlayerController>();
        switcher = FindFirstObjectByType<CharacterSwitcher>();
        if (kris == null) { enabled = false; return; }
        body = kris.GetComponent<Rigidbody2D>();
        col = kris.GetComponent<Collider2D>();
        krisSprite = kris.GetComponent<SpriteRenderer>();
        animator = kris.GetComponent<Animator>();
        cam = Camera.main;
        art = Resources.Load<FountainArt>("FountainArt");

        // His feet: the lowest visible pixel of his picture. The cutscene stands on exactly the same line, so Kris does not jump
        // or sink when the real Kris is swapped for the cutscene one and back.
        feetOffset = col.bounds.min.y - kris.transform.position.y;
        if (krisSprite.sprite != null)
        {
            float lowest = float.MaxValue;
            foreach (var v in krisSprite.sprite.vertices) lowest = Mathf.Min(lowest, v.y);
            feetOffset = lowest * kris.transform.lossyScale.y;
        }

        PowersUnlocked = false;                                          // nothing works until S is pressed at an obstacle
        SetHudAlpha(0f);                                                 // no portraits until the first fountain
        if (art == null || !art.Complete)
            Debug.LogWarning("KrisForms: no fountain art, so S arms the pad without the animation (Tools > Setup Fountain Art makes it).");
    }

    void RequestFountain()
    {
        if (kris == null || cutscene) return;
        if (PowersUnlocked) { Debug.Log("The pad is already out: focus on it, the Python T activates the power."); return; }
        if (!PowerTargets.Nearest(true, 0f, out _))
        {
            Debug.Log("S: no obstacle in sight needs a friend's power.");
            return;
        }
        if (!kris.IsGrounded)
        {
            // pressed in the air (a jump, a fall): the fountain starts the moment he lands, if that is soon
            waitForGround = Time.time + MaxWaitForGround;
            Debug.Log("S: the fountain starts when Kris is on the ground.");
            return;
        }
        waitForGround = 0f;
        Begin();
    }

    void Begin()
    {
        if (art == null || !art.Complete) { Arm(); return; }
        StartCoroutine(Fountain());
    }

    // S was pressed while Kris was in the air: start as soon as he stands on the ground (give up after a moment: he may be falling away).
    const float MaxWaitForGround = 3f;
    float waitForGround;

    void Update()
    {
        if (waitForGround <= 0f) return;
        if (cutscene || PowersUnlocked || kris == null || Time.time > waitForGround) { waitForGround = 0f; return; }
        if (!kris.IsGrounded) return;
        waitForGround = 0f;
        if (PowerTargets.Nearest(true, 0f, out _)) Begin();                                   // still at an obstacle after the landing
    }

    // ---- the dark fountain ----

    // The sound of the dark fountain (Resources/Audio/dark_fountain), about 11 s: it starts with the cutscene and fades out at its end.
    // It follows the sound effects volume and the mute of the menu settings (AudioManager); when GameScene is started directly there
    // is no AudioManager and a plain 2D sound is played.
    const string FountainSoundResource = "Audio/dark_fountain";
    static AudioClip fountainSound;

    // The game music waits while the fountain sound plays and goes on afterwards (it counts game time, so a pause waits too).
    void PlayFountainSound()
    {
        if (fountainSound == null) fountainSound = Resources.Load<AudioClip>(FountainSoundResource);
        if (fountainSound == null) return;
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx(fountainSound);
            AudioManager.Instance.PauseMusic();
            StartCoroutine(ResumeMusicAfter(fountainSound.length));
            return;
        }
        var go = new GameObject("Fountain sound");
        var source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.PlayOneShot(fountainSound, 0.8f);
        Destroy(go, fountainSound.length + 0.2f);
    }

    IEnumerator ResumeMusicAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (AudioManager.Instance != null) AudioManager.Instance.ResumeMusic();
    }

    IEnumerator Fountain()
    {
        cutscene = true;
        Covered = false;
        lifting = false;
        afterLift = null;
        smokeStop = false;
        sparkling = false;
        if (cam == null) cam = Camera.main;
        PlayFountainSound();
        if (LevelEnd.Instance != null) LevelEnd.Instance.Paused = true;          // the clock waits during the cutscene

        // Kris stops where he is
        kris.enabled = false;
        RemoteInput.SetHorizontal(0f);
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            if (Mathf.Abs(body.linearVelocity.y) < 0.05f) break;                  // wait until he is standing
            yield return null;
        }
        body.linearVelocity = Vector2.zero;
        body.bodyType = RigidbodyType2D.Kinematic;

        spot = kris.transform.position;
        feetY = kris.transform.position.y + feetOffset;
        layer = krisSprite.sortingLayerID;
        order = krisSprite.sortingOrder;

        // the real Kris steps aside and a copy of his (blue) picture plays the cutscene
        var idle = krisSprite.sprite;
        krisSprite.enabled = false;
        if (animator != null) animator.enabled = false;
        cut = NewSprite("Fountain Kris", idle, 90);
        Pose(idle, 0f);
        overlay = NewSprite("Fountain tint", FountainFx.White, 80);
        overlay.color = new Color(0.04f, 0.06f, 0.3f, 0f);

        // 1. the room goes dark, stars twinkle
        sparkling = true;
        StartCoroutine(Sparkle());
        yield return Fade(overlay, new Color(0.04f, 0.06f, 0.3f, 0.6f), 0.8f);

        // 2. Kris jumps up with the knife raised, his eyes glow red, and he hangs in the air for a moment
        float[] hop = { 0.15f, 0.35f, 0.5f, 0.45f };
        yield return Play(art.making, 7, 10, 0.15f, i => hop[i - 7]);
        for (int n = 0; n < 3; n++)
            yield return Play(art.making, 9, 10, 0.15f, i => i == 9 ? 0.5f : 0.45f);

        // 3. he stabs the floor: a white line runs along it, then a white ellipse, and Kris is a white silhouette on the cracks
        Pose(art.making[11], 0f);
        var line = NewSprite("Fountain line", FountainFx.White, 81);
        line.transform.position = new Vector3(spot.x, feetY + 0.04f, 0f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            line.transform.localScale = new Vector3(Mathf.Lerp(0.2f, 8f, t / 0.3f), 0.07f, 1f);
            Fit();
            yield return null;
        }
        Destroy(line.gameObject);
        var crack = NewSprite("Fountain cracks", art.making[22], 79);
        crack.transform.position = new Vector3(spot.x, feetY - (art.making[22].bounds.min.y + art.PadOf(art.making[22])), 0f);
        var glow = NewSprite("Fountain glow", FountainFx.Ellipse, 81);
        glow.transform.position = new Vector3(spot.x, feetY + 0.15f, 0f);
        glow.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
        yield return Play(art.making, 12, 15, 0.12f);
        sparkling = false;

        // 4. the room turns black and white and the beam of darkness comes down: white, then grey, then black
        yield return Fade(overlay, new Color(1f, 1f, 1f, 0.9f), 0.25f);
        float top = cam != null ? cam.transform.position.y + cam.orthographicSize + 1f : feetY + 9f;
        float beamHeight = Mathf.Max(6f, top - feetY);
        var beamOutline = NewSprite("Fountain beam outline", FountainFx.Beam, 82);
        var beamFill = NewSprite("Fountain beam", FountainFx.Beam, 83);
        beamOutline.transform.position = beamFill.transform.position = new Vector3(spot.x, feetY, 0f);
        float widthScale = 1.5f, heightScale = beamHeight / 12f;
        beamOutline.transform.localScale = new Vector3(widthScale * 1.05f, heightScale, 1f);
        beamFill.transform.localScale = new Vector3(widthScale, heightScale, 1f);
        beamOutline.color = Color.white;
        const float beamTime = 2.0f;
        for (float t = 0f; t < beamTime; t += Time.deltaTime)
        {
            float k = t / beamTime;
            beamFill.color = Color.Lerp(Color.white, Color.black, k);
            Pose(k < 0.65f ? art.making[16 + ((int)(t * 8f) & 1)] : art.making[18], 0f);       // cream silhouette, then black
            Fit();
            yield return null;
        }

        // 5. back to the dark room, the beam closes
        StartCoroutine(Fade(overlay, new Color(0.04f, 0.06f, 0.3f, 0.45f), 0.5f));
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            float s = Mathf.Lerp(widthScale, 0.05f, t / 0.35f);
            beamFill.transform.localScale = new Vector3(s, heightScale, 1f);
            beamOutline.transform.localScale = new Vector3(s * 1.05f + 0.02f, heightScale, 1f);
            yield return null;
        }
        Destroy(beamFill.gameObject);
        Destroy(beamOutline.gameObject);
        Destroy(glow.gameObject);

        // 6. Kris kneels where he stabbed the floor, and black smoke rises from that spot up to the top of the screen
        Pose(art.making[19], 0f);
        StartCoroutine(Smoke());
        yield return Wait(1.3f);

        // 7. the smoke spreads along the top of the screen, then comes down like a curtain until everything is black
        if (cam != null)
        {
            LivesHidden = true;                                                   // the hearts and the portraits fade away
            StartCoroutine(FadeHud(0f, 1f));
            BuildCurtain();
            yield return MoveCurtain(0f, 0f, 0f, 1f, 0.9f);
            yield return MoveCurtain(0f, 1f, 1f, 1f, 3.0f);
        }
        smokeStop = true;
        yield return null;

        // 8. behind the black screen the room is put back as it was; on the black screen the pad appears and flickers until T
        foreach (var go in stage) if (go != null) Destroy(go);
        stage.Clear();
        if (krisSprite != null) { krisSprite.enabled = true; krisSprite.flipX = false; }
        if (animator != null) animator.enabled = true;
        ShowPad(cam == null);
        if (cam != null)
        {
            Covered = true;
            if (LevelEnd.Instance != null) LevelEnd.Instance.Paused = false;     // the clock runs while the player focuses on the pad
            while (!lifting)
            {
                PlaceCurtain(1f, 1f);
                yield return null;
            }
            Covered = false;

            // 9. T: the pad is used up (it fades away), the smoke lifts and the game is back
            yield return MoveCurtain(1f, 0f, 1f, 1f, 1.4f);
            foreach (var go in curtainObjects) if (go != null) Destroy(go);
            curtainObjects.Clear();
            curtain = null;
        }

        // 10. Kris is free again, the hearts and portraits are back, and the friend arrives (see CharacterSwitcher.AutoSelect)
        LivesHidden = false;
        StartCoroutine(FadeHud(1f, 0.6f));
        Release();
        cutscene = false;
        if (LevelEnd.Instance != null) LevelEnd.Instance.Paused = false;
        var then = afterLift;
        afterLift = null;
        lifting = false;
        then?.Invoke();
    }

    // No animation (the fountain art is missing): the pad appears at once and Kris stays free.
    void Arm()
    {
        ShowPad();
        Release();
    }

    // The fountain's gift is switched on: the portraits are there, the pad appears, T works.
    void ShowPad(bool showHud = true)
    {
        DarkWorld = true;
        PowersUnlocked = true;
        if (showHud) StartCoroutine(FadeHud(1f, 0.6f));
        SsvepBoard.Create(switcher);
    }

    // Kris is a normal, controllable, animated Kris again.
    void Release()
    {
        if (body != null) { body.bodyType = RigidbodyType2D.Dynamic; body.linearVelocity = Vector2.zero; }
        if (krisSprite != null) { krisSprite.enabled = true; krisSprite.flipX = false; }
        if (animator != null) animator.enabled = true;
        if (kris != null) kris.enabled = true;
    }

    // ---- the pieces of the sequence ----

    SpriteRenderer NewSprite(string name, Sprite sprite, int orderOffset)
    {
        var go = new GameObject(name);
        stage.Add(go);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = layer;
        sr.sortingOrder = order + orderOffset;
        return sr;
    }

    // Empty space under the feet of a picture: measured for the fountain pictures; for Kris's own (tight mesh) it is the gap
    // between the bottom of the picture and its lowest vertex.
    float PadFor(Sprite s)
    {
        float known = art.PadOf(s);
        if (known > 0f) return known;
        float low = float.MaxValue;
        foreach (var v in s.vertices) low = Mathf.Min(low, v.y);
        return low - s.bounds.min.y;
    }

    // Draws Kris's cutscene picture standing on the floor (`lift` raises it).
    void Pose(Sprite s, float lift)
    {
        cut.sprite = s;
        cut.transform.position = new Vector3(spot.x, feetY - (s.bounds.min.y + PadFor(s)) + lift, 0f);   // the visible feet on the floor
    }

    // The tint covers exactly what the camera shows.
    void Fit()
    {
        if (overlay == null || cam == null) return;
        overlay.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        float h = cam.orthographicSize * 2f + 2f;
        overlay.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
    }

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Fit();
            yield return null;
        }
    }

    IEnumerator Fade(SpriteRenderer sr, Color to, float seconds)
    {
        Color from = sr.color;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (sr == null) yield break;
            sr.color = Color.Lerp(from, to, t / seconds);
            Fit();
            yield return null;
        }
        if (sr != null) sr.color = to;
    }

    // Plays frames from..to (inclusive) of a sheet, `each` seconds apart. lift(i) raises Kris above the floor for frame i.
    IEnumerator Play(Sprite[] frames, int from, int to, float each, Func<int, float> lift = null)
    {
        for (int i = from; i <= to; i++)
        {
            Pose(frames[i], lift != null ? lift(i) : 0f);
            yield return Wait(each);
        }
    }

    // Twinkling stars round Kris (the Fountain_opening_eff pictures).
    IEnumerator Sparkle()
    {
        while (sparkling)
        {
            var s = art.sparkles[UnityEngine.Random.Range(0, art.sparkles.Length)];
            Vector2 offset = UnityEngine.Random.insideUnitCircle * 1.6f;
            StartCoroutine(Twinkle(s, new Vector3(spot.x + offset.x, feetY + 1.3f + offset.y, 0f)));
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.04f, 0.1f));
        }
    }

    IEnumerator Twinkle(Sprite s, Vector3 at)
    {
        var sr = NewSprite("Fountain star", s, 92);
        sr.transform.position = at;
        float size = UnityEngine.Random.Range(0.5f, 1f);
        sr.transform.localScale = new Vector3(size, size, 1f);
        float life = UnityEngine.Random.Range(0.3f, 0.6f);
        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            if (sr == null) yield break;
            var c = sr.color;
            c.a = Mathf.Sin(t / life * Mathf.PI);
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    // ---- the smoke ----
    // All of it is drawn on the top sorting layer, above everything of the level (the HUD and the pad are above it: they are on
    // screen canvases). Every white outline is behind every black shape, so only the outside of the whole cloud has an outline.
    const int SmokeOutlineOrder = 32000, SmokeFillOrder = 32002;

    static int TopLayer()
    {
        var layers = SortingLayer.layers;
        return layers.Length > 0 ? layers[layers.Length - 1].id : 0;
    }

    SpriteRenderer NewSmoke(string name, Sprite sprite, bool outline, List<GameObject> list)
    {
        var go = new GameObject(name);
        list.Add(go);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = TopLayer();
        sr.sortingOrder = outline ? SmokeOutlineOrder : SmokeFillOrder;
        sr.color = outline ? Color.white : Color.black;
        return sr;
    }

    // The column: puffs rise from the spot Kris stabbed and pile up into a column that reaches above the top of the screen.
    class Puff { public Transform outline, fill; public float born, speed, size, x, drift; }
    bool smokeStop;

    IEnumerator Smoke()
    {
        var puffs = new List<Puff>();
        float nextSpawn = 0f;
        while (!smokeStop)
        {
            float now = Time.time;
            float riseLimit = cam != null ? cam.transform.position.y + cam.orthographicSize + 2f - feetY : 11f;
            if (now >= nextSpawn)
            {
                nextSpawn = now + 0.04f;
                var p = new Puff
                {
                    born = now,
                    speed = UnityEngine.Random.Range(4.5f, 7.5f),
                    size = UnityEngine.Random.Range(0.7f, 1.15f),
                    x = UnityEngine.Random.Range(-0.45f, 0.45f),
                    drift = UnityEngine.Random.Range(-0.2f, 0.2f),
                };
                p.outline = NewSmoke("Smoke outline", FountainFx.Disc, true, stage).transform;
                p.fill = NewSmoke("Smoke", FountainFx.Disc, false, stage).transform;
                puffs.Add(p);
            }
            foreach (var p in puffs)
            {
                if (p.fill == null || p.outline == null) continue;
                float age = now - p.born;
                var at = new Vector3(spot.x + p.x + p.drift * age, feetY + 0.3f + Mathf.Min(p.speed * age, riseLimit), 0f);
                float scale = p.size * Mathf.Clamp01(age / 0.4f);
                p.fill.position = p.outline.position = at;
                p.fill.localScale = Vector3.one * scale;
                p.outline.localScale = Vector3.one * (scale + 0.1f);
            }
            yield return null;
        }
        foreach (var p in puffs)
        {
            if (p.outline != null) Destroy(p.outline.gameObject);
            if (p.fill != null) Destroy(p.fill.gameObject);
        }
    }

    // The curtain: a black block with a row of round puffs along its lower edge (the scalloped cloud front of the video). It hangs
    // from the camera: `cover` 0 = only the puffs show at the top of the screen, 1 = the whole screen is black. `spread` 0..1 lets
    // the puffs grow outwards from the column.
    class CurtainParts { public Transform block; public Transform[] outline, fill; public float[] x, y, size, phase; }
    CurtainParts curtain;
    readonly List<GameObject> curtainObjects = new List<GameObject>();
    const float DiscRadius = 1.5f;                                 // FountainFx.Disc at scale 1

    void BuildCurtain()
    {
        foreach (var go in curtainObjects) if (go != null) Destroy(go);
        curtainObjects.Clear();

        float halfW = cam.orthographicSize * cam.aspect + 2f;
        const float spacing = 2.1f;
        int count = Mathf.CeilToInt(halfW * 2f / spacing) + 1;
        curtain = new CurtainParts
        {
            outline = new Transform[count], fill = new Transform[count],
            x = new float[count], y = new float[count], size = new float[count], phase = new float[count],
        };
        curtain.block = NewSmoke("Smoke curtain", FountainFx.White, false, curtainObjects).transform;
        for (int i = 0; i < count; i++)
        {
            curtain.outline[i] = NewSmoke("Smoke curtain outline", FountainFx.Disc, true, curtainObjects).transform;
            curtain.fill[i] = NewSmoke("Smoke curtain puff", FountainFx.Disc, false, curtainObjects).transform;
            curtain.x[i] = -halfW + i * spacing + UnityEngine.Random.Range(-0.25f, 0.25f);
            curtain.y[i] = UnityEngine.Random.Range(-0.25f, 0.25f);
            curtain.size[i] = UnityEngine.Random.Range(0.85f, 1.2f);
            curtain.phase[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }
        PlaceCurtain(0f, 0f);
    }

    void PlaceCurtain(float cover, float spread)
    {
        if (curtain == null || cam == null) return;
        Vector3 c = cam.transform.position;
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        // the row of puffs: just above the top of the screen (only their lower halves show) ... well below its bottom
        float edge = Mathf.Lerp(c.y + halfH + 0.3f, c.y - halfH - DiscRadius * 1.2f - 1f, cover);

        float blockHeight = halfH * 2f + 8f;
        curtain.block.position = new Vector3(c.x, edge + 0.4f + blockHeight / 2f, 0f);
        curtain.block.localScale = new Vector3(halfW * 2f + 6f, blockHeight, 1f);

        float reach = halfW + 3f + Mathf.Abs(spot.x - c.x);        // how far the smoke has to run from the column
        for (int i = 0; i < curtain.fill.Length; i++)
        {
            float x = c.x + curtain.x[i];
            float grown = Mathf.Clamp01((spread * (reach + 2.5f) - Mathf.Abs(x - spot.x)) / 2.5f);
            float scale = curtain.size[i] * grown;
            var at = new Vector3(x, edge + curtain.y[i] + 0.12f * Mathf.Sin(Time.time * 2f + curtain.phase[i]), 0f);
            curtain.fill[i].position = curtain.outline[i].position = at;
            curtain.fill[i].localScale = Vector3.one * scale;
            curtain.outline[i].localScale = Vector3.one * (grown > 0f ? scale + 0.1f : 0f);
        }
    }

    IEnumerator MoveCurtain(float coverFrom, float coverTo, float spreadFrom, float spreadTo, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            PlaceCurtain(Mathf.Lerp(coverFrom, coverTo, Mathf.SmoothStep(0f, 1f, k)), Mathf.Lerp(spreadFrom, spreadTo, k));
            Fit();
            yield return null;
        }
        PlaceCurtain(coverTo, spreadTo);
    }

    // ---- the HUD (portraits) ----

    void SetHudAlpha(float a)
    {
        if (hud == null && switcher != null)
        {
            var canvas = switcher.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.gameObject : switcher.gameObject;
            hud = root.GetComponent<CanvasGroup>();
            if (hud == null) hud = root.AddComponent<CanvasGroup>();
        }
        if (hud != null) hud.alpha = a;
    }

    IEnumerator FadeHud(float to, float seconds)
    {
        SetHudAlpha(hud != null ? hud.alpha : 0f);
        float from = hud != null ? hud.alpha : 0f;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetHudAlpha(Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        SetHudAlpha(to);
    }
}
