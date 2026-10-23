using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Noelle's power: she turns water into ice. When she is brought in she appears at the edge of the water pool nearest to
// Kris that is not frozen yet, and ice grows over the surface of the pool, one tile after another. The ice is solid
// (Ground layer), so Kris can walk across; the water under it stays. Then she is done and disappears; the next time she is
// brought in she takes the next pool. Any number of pools work, nothing is assigned per pool: a pool is a run of side by
// side columns in Tilemap_Water, its surface is the highest water tile of each column.
[RequireComponent(typeof(SpriteRenderer))]
public class NoelleFreeze : MonoBehaviour
{
    [SerializeField] CharacterSwitcher switcher;
    [Tooltip("Seconds she stands still before the ice starts.")]
    [SerializeField] float castDelay = 0.7f;
    [Tooltip("Seconds between one ice tile and the next.")]
    [SerializeField] float tileDelay = 0.06f;
    [Tooltip("Seconds the snowstorm takes to fade out once she is gone (she disappears the moment the ice is done).")]
    [SerializeField] float stormFadeSeconds = 0.9f;
    [Tooltip("Safety: she is put away after this many seconds even if something went wrong.")]
    [SerializeField] float maxCastSeconds = 15f;
    [Tooltip("Gap between her front and the edge of the pool (world units); 0 = touching, like Susie at the box.")]
    [SerializeField] float edgeGap = 0f;
    [Tooltip("Nudge if her feet float above or sink below the ground (world units).")]
    [SerializeField] float footAdjust = 0f;
    [Tooltip("Colour of the ice. The alpha is how see-through it is (0.6 = frosted, 1 = solid block).")]
    [SerializeField] Color iceTint = new Color(0.86f, 0.93f, 0.97f, 0.6f);

    [Header("Casting animation")]
    [Tooltip("Her spell frames in order (Assets/Characters/Noelle/Cast). Empty = she keeps her normal idle animation.")]
    [SerializeField] Sprite[] castFrames;
    [SerializeField] float frameRate = 10f;
    [Tooltip("The frame where the spell starts and the ice begins to grow (the first one with sparkles).")]
    [SerializeField] int spellFrame = 7;
    [Tooltip("The spell frames (spellFrame up to the one before this) repeat until the ice is done; the frames from here on are not played, she disappears at once.")]
    [SerializeField] int releaseFrame = 14;
    [Header("Floating")]
    [Tooltip("How high she floats up while she casts (world units).")]
    [SerializeField] float riseHeight = 1f;
    [Tooltip("How far she drifts towards the pool while she casts (world units).")]
    [SerializeField] float driftToPool = 0.8f;
    [Tooltip("Seconds she floats and sparkles before the snowstorm starts.")]
    [SerializeField] float hoverSeconds = 1.4f;

    const string WaterName = "Tilemap_Water", IceName = "Tilemap_Ice";

    SpriteRenderer sprite;
    Animator animator;
    float feetBelowPivot;
    Tilemap water, ice;
    Tile iceTile;
    Collider2D ownCollider;

    struct Column { public int x, y; public bool frozen; }
    List<Column> pool;             // the pool she is freezing, in the order the ice grows
    bool done;
    bool facingRight;              // which way she faces while casting (towards the pool)
    bool snowing;                  // the snowstorm stays while this is true
    bool sparkling;                // stars keep twinkling round her while this is true
    bool casting;                  // her position is driven by LateUpdate (floating) while this is true
    Vector3 basePosition;          // where she stands on the ground
    float lift, drift;             // how far she floats up / towards the pool
    NoelleEffects fx;              // optional: the orb, snowflakes and snow (see NoelleEffects)

    void Awake()
    {
        sprite = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        ownCollider = GetComponent<Collider2D>();
        fx = GetComponent<NoelleEffects>();
        var follow = GetComponent<CompanionFollow>();           // she does not follow Kris around any more
        if (follow != null) follow.enabled = false;

        ShowCastFrame(0);                                       // from now on she is drawn from her spell frames
        float lowest = 0f;
        if (sprite.sprite != null)
        {
            lowest = float.MaxValue;
            foreach (var v in sprite.sprite.vertices) lowest = Mathf.Min(lowest, v.y);
        }
        feetBelowPivot = lowest * transform.lossyScale.y;
    }

    bool HasCastFrames => castFrames != null && castFrames.Length > 0;

    // Draws one of her spell frames (the animator is switched off, otherwise it would draw over them every frame).
    void ShowCastFrame(int index)
    {
        if (!HasCastFrames) return;
        if (animator != null) animator.enabled = false;
        sprite.sprite = castFrames[Mathf.Clamp(index, 0, castFrames.Length - 1)];
    }

    void OnEnable()
    {
        done = false;
        sprite.enabled = true;
        if (!FindTilemaps() || !ChoosePool(out bool fromLeft))
        {
            done = true;                                           // nothing left to freeze: show nothing, Update puts her away
            sprite.enabled = false;
            return;
        }
        Place(fromLeft);
        StartCoroutine(Cast(fromLeft));
    }

    void Update()
    {
        if (done) Finish();
    }

    // ---- finding the tilemaps and the pool ----

    // Just finds the two tilemaps (no side effects).
    bool LocateTilemaps()
    {
        water = ice = null;
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.name == WaterName) water = tm;
            else if (tm.name == IceName) ice = tm;
        }
        return water != null && ice != null;
    }

    // Is there a pool that still needs her? x = where it is (its near edge, or Kris's x when he is above it). See PowerTargets.
    public bool FindWork(float krisX, bool onScreenOnly, float lookAhead, out float x)
    {
        x = 0f;
        if (!LocateTilemaps() || !PickPool(krisX, onScreenOnly, lookAhead, out var best, out float left, out float right)) return false;
        x = krisX < left ? left : krisX > right ? right : krisX;
        return true;
    }

    bool FindTilemaps()
    {
        if (!LocateTilemaps())
        {
            Debug.LogWarning($"NoelleFreeze needs tilemaps called {WaterName} and {IceName}.");
            return false;
        }
        MakeIceSolid();
        if (iceTile == null) iceTile = MakeIceTile();
        return iceTile != null;
    }

    // The ice has to hold Kris up: a solid collider on the Ground layer (the layer Kris's ground check looks at),
    // drawn just in front of the water.
    void MakeIceSolid()
    {
        var go = ice.gameObject;
        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) go.layer = ground;
        if (go.GetComponent<TilemapCollider2D>() == null)
        {
            var tile = go.AddComponent<TilemapCollider2D>();
            go.AddComponent<CompositeCollider2D>();                // brings a Rigidbody2D along
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null) rb.bodyType = RigidbodyType2D.Static;
            tile.compositeOperation = Collider2D.CompositeOperation.Merge;
        }
        // the ice is a floor, so it is drawn behind Kris (the water is drawn in front of him, see LivesManager)
        var iceRenderer = ice.GetComponent<TilemapRenderer>();
        var kris = FindFirstObjectByType<PlayerController>();
        var krisRenderer = kris != null ? kris.GetComponent<SpriteRenderer>() : null;
        if (iceRenderer != null && krisRenderer != null)
        {
            iceRenderer.sortingLayerID = krisRenderer.sortingLayerID;
            iceRenderer.sortingOrder = krisRenderer.sortingOrder - 1;
        }
    }

    // A pixel art ice block drawn here, the same size as one water tile, so no extra art has to be imported.
    Tile MakeIceTile()
    {
        Sprite reference = null;
        foreach (var cell in water.cellBounds.allPositionsWithin)
        {
            reference = water.GetSprite(cell);
            if (reference != null) break;
        }
        int size = reference != null ? Mathf.RoundToInt(reference.rect.width) : 16;
        float ppu = reference != null ? reference.pixelsPerUnit : size;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        // frosted glass: a see-through pale sheet (the water or sky shows through), a bright top edge, faint lighter bands
        var bright = new Color(1f, 1f, 1f, Mathf.Min(1f, iceTint.a + 0.3f));
        var band = Color.Lerp(iceTint, Color.white, 0.5f);
        band.a = Mathf.Min(1f, iceTint.a + 0.12f);
        var edge = Color.Lerp(iceTint, new Color(0.45f, 0.65f, 0.8f), 0.5f);   // thin darker line at the bottom
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color c = iceTint;
                int fromTop = size - 1 - y;
                if (fromTop == 0) c = bright;                                  // bright top edge
                else if (y == 0) c = edge;
                else if (fromTop % 4 == 2) c = band;                           // faint horizontal bands
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();

        var t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        t.colliderType = Tile.ColliderType.Grid;                               // a full square, so Kris can stand on it
        return t;
    }

    // The nearest pool to Kris that still has water at its surface; true if there is one, and which side Kris is on.
    bool ChoosePool(out bool fromLeft)
    {
        fromLeft = true;
        var kris = FindFirstObjectByType<PlayerController>();
        float krisX = kris != null ? kris.transform.position.x : transform.position.x;
        if (!PickPool(krisX, true, 0f, out var best, out _, out _)) return false;

        float centre = (water.CellToWorld(new Vector3Int(best[0].x, 0, 0)).x +
                        water.CellToWorld(new Vector3Int(best[best.Count - 1].x + 1, 0, 0)).x) / 2f;
        fromLeft = krisX < centre;                                             // she stands on Kris's side of the pool
        pool = best;
        if (!fromLeft) pool.Reverse();                                         // the ice grows away from her
        return true;
    }

    // The nearest pool that still has water at its surface: only one the camera shows (onScreenOnly), or any up to lookAhead
    // units in front of Kris. Changes nothing.
    bool PickPool(float krisX, bool onScreenOnly, float lookAhead, out List<Column> best, out float bestLeft, out float bestRight)
    {
        best = null;
        bestLeft = bestRight = 0f;

        // surface of every column: the highest water or ice tile; frozen when that tile is ice
        var surface = new SortedDictionary<int, Column>();
        foreach (var cell in water.cellBounds.allPositionsWithin)
            if (water.HasTile(cell)) Raise(surface, cell.x, cell.y, false);
        foreach (var cell in ice.cellBounds.allPositionsWithin)
            if (ice.HasTile(cell)) Raise(surface, cell.x, cell.y, true);

        // pools = runs of neighbouring columns
        var pools = new List<List<Column>>();
        List<Column> run = null;
        int last = int.MinValue;
        foreach (var kv in surface)
        {
            if (run == null || kv.Key != last + 1)
            {
                run = new List<Column>();
                pools.Add(run);
            }
            run.Add(kv.Value);
            last = kv.Key;
        }

        float bestDistance = float.MaxValue;
        foreach (var p in pools)
        {
            if (!p.Exists(c => !c.frozen)) continue;                           // already ice all the way
            float left = water.CellToWorld(new Vector3Int(p[0].x, 0, 0)).x;
            float right = water.CellToWorld(new Vector3Int(p[p.Count - 1].x + 1, 0, 0)).x;
            bool wanted = onScreenOnly ? WorkRange.OverlapsScreen(left, right)  // only a pool the player can see
                                       : PowerTargets.InZone(left, right, krisX, lookAhead);
            if (!wanted) continue;
            float distance = krisX < left ? left - krisX : krisX > right ? krisX - right : 0f;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = p;
                bestLeft = left;
                bestRight = right;
            }
        }
        return best != null;
    }

    static void Raise(SortedDictionary<int, Column> surface, int x, int y, bool isIce)
    {
        if (!surface.TryGetValue(x, out var c) || y > c.y || (y == c.y && isIce))
            surface[x] = new Column { x = x, y = y, frozen = isIce };
    }

    // ---- standing at the pool and freezing it ----

    void Place(bool fromLeft)
    {
        var first = pool[0];                                                    // the column nearest to her
        float edgeX = fromLeft
            ? water.CellToWorld(new Vector3Int(first.x, 0, 0)).x
            : water.CellToWorld(new Vector3Int(first.x + 1, 0, 0)).x;
        // her front (the tip of the sprite on the side she faces) goes right up to the edge of the water, no gap
        float reach = FrontReach(facingRight: fromLeft);
        float x = edgeX + (fromLeft ? -(reach + edgeGap) : reach + edgeGap);
        float surfaceY = water.CellToWorld(new Vector3Int(first.x, first.y + 1, 0)).y;

        float ground = surfaceY;
        foreach (var hit in Physics2D.RaycastAll(new Vector2(x, surfaceY + 3f), Vector2.down, 12f))
        {
            var c = hit.collider;
            if (c.isTrigger || c == ownCollider || c.attachedRigidbody != null && c.attachedRigidbody.GetComponent<PlayerController>() != null) continue;
            ground = hit.point.y;
            break;
        }
        transform.position = new Vector3(x, ground - feetBelowPivot + footAdjust, transform.position.z);
        basePosition = transform.position;
        sprite.flipX = !fromLeft;                                              // faces the pool
        facingRight = fromLeft;
        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null)
        {
            animator.SetBool("IsRunning", false);
            animator.SetBool("IsGrounded", true);
            animator.SetFloat("YVelocity", 0f);
        }
    }

    // Distance from her pivot to the tip of her sprite on the side she faces (the sprite is drawn facing right; flipX turns it).
    float FrontReach(bool facingRight)
    {
        // the farthest her hands reach in any spell frame, so the arm never goes into the water's edge
        var sprites = HasCastFrames ? castFrames : new[] { sprite.sprite };
        float front = 0f;
        foreach (var s in sprites)
        {
            if (s == null) continue;
            foreach (var v in s.vertices)
                front = Mathf.Max(front, facingRight ? v.x : -v.x);
        }
        return front * transform.lossyScale.x;
    }

    IEnumerator Cast(bool fromLeft)
    {
        yield return null;                                                      // let anything that moves her on wake-up finish first
        ShowCastFrame(0);
        Place(fromLeft);
        lift = drift = 0f;
        casting = sparkling = true;

        // Like the SNOWGRAVE in the game: a white flash, then she stands in her spell pose while stars twinkle round her
        if (fx != null)
        {
            fx.Burst(sprite.bounds.center, 2.4f, 0.3f, 0.8f);
            StartCoroutine(fx.Sparkles(transform, 1.7f, () => sparkling));
        }

        if (!HasCastFrames)
        {
            // no spell frames: stand still, then freeze (her normal idle animation keeps playing)
            yield return new WaitForSeconds(castDelay);
            yield return Spell();
            yield return Vanish();
            yield break;
        }

        float frameTime = 1f / Mathf.Max(1f, frameRate);
        int spell = Mathf.Clamp(spellFrame, 0, castFrames.Length - 1);
        int release = Mathf.Clamp(releaseFrame, spell + 1, castFrames.Length);

        for (int i = 0; i < spell; i++)                                         // wind-up
        {
            ShowCastFrame(i);
            yield return new WaitForSeconds(frameTime);
        }

        bool frozenAll = false;
        StartCoroutine(Hover(riseHeight, driftToPool, hoverSeconds));          // she floats up and towards the pool
        StartCoroutine(FreezeThen(() => frozenAll = true));                     // the storm, the column and the ice play while the spell frames repeat
        int frame = spell;
        float started = Time.time;
        while (true)
        {
            ShowCastFrame(frame);
            yield return new WaitForSeconds(frameTime);
            frame++;
            if (frozenAll || Time.time - started > maxCastSeconds) break;       // the ice is all there: she is done at once (the time limit is only a safety)
            if (frame >= release) frame = spell;                                // not all the ice is there yet: cast again
        }
        yield return Vanish();
    }

    // She is gone the moment the job is done, like Susie once the box has landed: no waiting on her last pose. A small white
    // burst marks where she was. The object stays on for a moment only so that the snowstorm can fade out.
    IEnumerator Vanish()
    {
        Vector3 centre = sprite.bounds.center;
        sparkling = false;
        sprite.enabled = false;
        if (fx != null) fx.Burst(centre, 2f, 0.25f, 0.8f);
        yield return new WaitForSeconds(stormFadeSeconds);
        casting = false;
        done = true;
    }

    // ---- floating: she rises and drifts a little towards the pool while she casts (as in the game) ----

    void LateUpdate()
    {
        if (!casting) return;
        float bob = Mathf.Sin(Time.time * 3f) * 0.05f * Mathf.Clamp01(lift);
        transform.position = basePosition + new Vector3((facingRight ? 1f : -1f) * drift, lift + bob, 0f);
    }

    IEnumerator Hover(float targetLift, float targetDrift, float seconds)
    {
        float fromLift = lift, fromDrift = drift;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            lift = Mathf.Lerp(fromLift, targetLift, k);
            drift = Mathf.Lerp(fromDrift, targetDrift, k);
            yield return null;
        }
        lift = targetLift;
        drift = targetDrift;
    }

    void OnDisable()
    {
        casting = sparkling = snowing = false;                                  // switched away in the middle: stop the effects
    }

    IEnumerator FreezeThen(System.Action finished)
    {
        try { yield return Spell(); }
        finally { finished(); }                                                 // even if something in the spell fails she must not get stuck
    }

    // The whole spell, in the order of the SNOWGRAVE in the game: she floats and sparkles for a moment, a dark snowstorm
    // fades in over the screen, a column of ice slams down on the first tile of the pool with a flash, and the ice grows
    // over the pool, a snowflake popping on every tile. Then the storm fades away again.
    IEnumerator Spell()
    {
        Column firstToFreeze = pool.Find(c => !c.frozen);
        var firstCell = new Vector3Int(firstToFreeze.x, firstToFreeze.y, 0);
        Vector3 impact = water.GetCellCenterWorld(firstCell);

        if (fx != null)
        {
            yield return new WaitForSeconds(hoverSeconds);                      // she rises, the stars twinkle
            snowing = true;
            StartCoroutine(fx.Storm(() => snowing));                            // the snowstorm fades in
            yield return new WaitForSeconds(0.5f);

            var cam = Camera.main;
            float top = cam != null ? cam.transform.position.y + cam.orthographicSize + 1f : impact.y + 10f;
            yield return fx.Column(impact, 1.6f, Mathf.Max(3f, top - impact.y)); // the ice column slams down
            fx.Burst(impact, 3.2f, 0.35f, 0.9f);
            fx.Patch(impact);
        }

        yield return Freeze();
        yield return new WaitForSeconds(0.2f);                                  // a moment to look at the ice, then the storm fades
        snowing = false;
    }

    // Where her outstretched hand is (the spell starts there).
    Vector3 HandPosition()
    {
        var b = sprite.bounds;
        float dir = facingRight ? 1f : -1f;
        return new Vector3(transform.position.x + dir * FrontReach(facingRight) * 0.9f, b.center.y + b.extents.y * 0.15f, 0f);
    }

    // The ice grows over the pool from Noelle's side, one surface tile after another.
    IEnumerator Freeze()
    {
        foreach (var column in pool)
        {
            if (column.frozen) continue;
            var cell = new Vector3Int(column.x, column.y, 0);
            water.SetTile(cell, null);                                          // the surface tile becomes ice
            ice.SetTile(cell, iceTile);
            ice.SetTileFlags(cell, TileFlags.None);
            ice.SetColor(cell, Color.white);
            if (fx != null) fx.Snowflake(water.GetCellCenterWorld(cell));
            yield return new WaitForSeconds(tileDelay);
        }
    }

    // Hide her the same way pressing Z would, so the switcher knows nobody is on screen.
    void Finish()
    {
        done = false;
        if (switcher != null && switcher.IsShown) switcher.Confirm();
        else gameObject.SetActive(false);
    }
}
