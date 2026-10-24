using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// The dark fountain. Kris wears his BLUE suit from the start and for the whole game. At every obstacle that needs a friend's power
// (a pool, a tree, a box) the player presses S (the Python S key, or S in Unity) and the DARK FOUNTAIN plays where Kris stands: the
// room goes dark, stars twinkle, a white flash, a beam of darkness, then smoke covers Kris and he jumps out of it. Only then the
// flickering SSVEP pad appears (SsvepBoard) and the power can be activated: the Python T (the classification says the player
// kept the focus) brings in the friend the obstacle needs (CharacterSwitcher.AutoSelect). That uses the pad up: the pad goes away
// and the next obstacle starts with S again.
//
// It builds itself when GameScene loads (like LevelEnd), so it survives merges. The sprites come from FountainArt.
// If the art is missing, S arms the pad at once, without the animation.
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
        if (scene.name != GameSceneName || FindFirstObjectByType<KrisForms>() != null) return;
        new GameObject("Kris Forms").AddComponent<KrisForms>();
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PowersUnlocked = true;
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
        PlayFountainSound();
        dissipate = false;                                                         // (it may still be set from the last time)
        smokeRunning = false;
        sparkling = false;
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
        yield return Wait(0.3f);

        // 2. Kris hops, his eyes glow red
        float[] hop = { 0f, 0.25f, 0.4f, 0.2f };
        yield return Play(art.making, 7, 10, 0.2f, i => hop[i - 7]);

        // 3. a white ellipse on the floor, Kris as a white silhouette on the cracks
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

        // 6. smoke rises and covers Kris (he stays blue: the light world frames of the sheet are not used)
        cut.sprite = idle;
        Pose(idle, 0f);
        StartCoroutine(Smoke());
        yield return Wait(2.0f);

        // 7. he jumps out of the smoke, curls into a ball, lands
        cut.sortingOrder = order + 200;                                           // in front of the smoke now
        yield return Play(art.krisTransform, 14, 18, 0.1f, i => 2.6f * (i - 13) / 5f);
        yield return Play(art.krisTransform, 19, 19, 0.12f, i => 2.7f);
        yield return Play(art.krisTransform, 20, 23, 0.09f, i => Mathf.Lerp(2.5f, 0.4f, (i - 20) / 3f));
        dissipate = true;                                                         // the smoke thins out as he lands
        yield return Play(art.krisTransform, 24, 25, 0.12f);
        yield return Play(art.krisTransform, 26, 26, 0.25f);
        while (smokeRunning) yield return null;

        // 8. Kris is free again, the portraits are back and the pad appears
        foreach (var go in stage) if (go != null) Destroy(go);
        stage.Clear();
        Arm();
        cutscene = false;
        if (LevelEnd.Instance != null) LevelEnd.Instance.Paused = false;
    }

    // Kris is a normal, controllable, animated Kris again, and the fountain's gift is switched on: the pad appears, T works.
    void Arm()
    {
        DarkWorld = true;
        PowersUnlocked = true;
        if (body != null) { body.bodyType = RigidbodyType2D.Dynamic; body.linearVelocity = Vector2.zero; }
        if (krisSprite != null) { krisSprite.enabled = true; krisSprite.flipX = false; }
        if (animator != null) animator.enabled = true;
        if (kris != null) kris.enabled = true;
        StartCoroutine(FadeHud(1f, 0.6f));
        SsvepBoard.Create(switcher);
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

    // The smoke: puffs (white outline behind, black fill in front, so only the outside of the cloud has an outline) rise from
    // Kris's feet and pile up into a column that covers him. `dissipate` makes them shrink away.
    class Puff { public Transform outline, fill; public float born, speed, size, x, drift; }
    bool dissipate, smokeRunning;

    IEnumerator Smoke()
    {
        smokeRunning = true;
        var puffs = new List<Puff>();
        float nextSpawn = 0f, dissipateStart = -1f;
        float riseLimit = cam != null ? cam.orthographicSize * 1.6f : 11f;

        while (true)
        {
            float now = Time.time;
            if (!dissipate && now >= nextSpawn)
            {
                nextSpawn = now + 0.04f;
                var p = new Puff
                {
                    born = now,
                    speed = UnityEngine.Random.Range(3.2f, 5.5f),
                    size = UnityEngine.Random.Range(0.7f, 1.1f),
                    x = UnityEngine.Random.Range(-0.35f, 0.35f),
                    drift = UnityEngine.Random.Range(-0.15f, 0.15f),
                };
                p.outline = NewSprite("Smoke outline", FountainFx.Disc, 95).transform;
                p.fill = NewSprite("Smoke", FountainFx.Disc, 97).transform;
                p.outline.GetComponent<SpriteRenderer>().color = Color.white;
                p.fill.GetComponent<SpriteRenderer>().color = Color.black;
                puffs.Add(p);
            }
            if (dissipate && dissipateStart < 0f) dissipateStart = now;

            float shrink = dissipateStart < 0f ? 1f : Mathf.Clamp01(1f - (now - dissipateStart) / 0.7f);
            foreach (var p in puffs)
            {
                float age = now - p.born;
                var at = new Vector3(spot.x + p.x + p.drift * age, feetY + 0.3f + Mathf.Min(p.speed * age, riseLimit), 0f);
                float scale = p.size * Mathf.Clamp01(age / 0.5f) * shrink;
                p.fill.position = p.outline.position = at;
                p.fill.localScale = Vector3.one * scale;
                p.outline.localScale = Vector3.one * (scale * 1.12f);
            }
            if (dissipateStart >= 0f && now - dissipateStart > 0.75f) break;
            yield return null;
        }
        foreach (var p in puffs)
        {
            if (p.outline != null) Destroy(p.outline.gameObject);
            if (p.fill != null) Destroy(p.fill.gameObject);
        }
        smokeRunning = false;
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
