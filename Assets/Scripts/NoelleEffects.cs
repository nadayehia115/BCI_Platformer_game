using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The visual effects of Noelle's ice power (art from the effects sheet, cut out by tools/extract_noelle_effects.py):
//   Orb        a striped ice orb flies from her hand to the pool
//   Flash      a white disc bursts where it lands
//   Patch      the frozen puddle appears there and fades
//   Snowflake  a snowflake pops over every tile that freezes
//   SnowFall   snow dots drift down over the pool while the ice grows
// Every effect is a throw-away sprite object that removes itself. NoelleFreeze calls these; the sprites are assigned by
// Tools > Setup Noelle Ice.
public class NoelleEffects : MonoBehaviour
{
    [SerializeField] Sprite orb;
    [SerializeField] Sprite flash;
    [SerializeField] Sprite patch;
    [SerializeField] Sprite snowflake;
    [SerializeField] Sprite snowA;
    [SerializeField] Sprite snowB;

    SpriteRenderer own;
    readonly List<GameObject> spawned = new List<GameObject>();     // everything on screen right now, so it can be cleared

    void Awake() => own = GetComponent<SpriteRenderer>();

    // The effects are coroutines on this object: if Noelle is switched away in the middle of the spell they stop, so what
    // they had put on screen (stars, the column, the snowstorm) is removed here instead of staying forever.
    void OnDisable()
    {
        foreach (var go in spawned)
            if (go != null) Destroy(go);
        spawned.Clear();
    }

    // ---- one-off effects ----

    // The orb flies from `from` to `to` in `seconds` along a small arc, spinning. Wait for it with yield return.
    public IEnumerator Orb(Vector3 from, Vector3 to, float seconds)
    {
        if (orb == null) yield break;
        var go = Spawn(orb, from, 0.7f, out var sr);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            go.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.8f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, k * 360f);
            yield return null;
        }
        Destroy(go);
    }

    // A white disc that grows and fades: the burst where the orb lands.
    public void Flash(Vector3 at) { if (flash != null) StartCoroutine(Grow(flash, at, 0.5f, 3.2f, 0.35f, 0.9f)); }

    // A white disc of the given size that grows and fades (Noelle's white flash when she starts the spell).
    public void Burst(Vector3 at, float size, float seconds, float alpha)
    {
        if (flash != null) StartCoroutine(Grow(flash, at, size * 0.5f, size, seconds, alpha));
    }

    // The frozen puddle: pops up, then fades.
    public void Patch(Vector3 at) { if (patch != null) StartCoroutine(Grow(patch, at, 1.2f, 2.4f, 1.6f, 0.95f)); }

    // A snowflake that pops, turns a quarter, drifts up and fades.
    public void Snowflake(Vector3 at)
    {
        if (snowflake != null) StartCoroutine(Pop(at));
    }

    // Snow dots drifting down over the stretch left..right (world x) from `top` while keepGoing() is true.
    public IEnumerator SnowFall(float left, float right, float top, Func<bool> keepGoing)
    {
        if (snowA == null && snowB == null) yield break;
        while (keepGoing())
        {
            var sprite = snowA != null && (snowB == null || UnityEngine.Random.value < 0.5f) ? snowA : snowB;
            float x = UnityEngine.Random.Range(left, right);
            StartCoroutine(Fall(sprite, new Vector3(x, top + 0.5f, 0f)));
            yield return new WaitForSeconds(0.15f);
        }
    }

    // ---- the SNOWGRAVE look: sparkles, snowstorm over the screen, ice column ----

    Sprite plusStar, crossStar, dotStar;      // tiny pixel art stars, drawn in code

    // Twinkling white stars (plus shapes, X shapes and dots) appearing around `centre` while keepGoing() is true.
    public IEnumerator Sparkles(Transform centre, float radius, Func<bool> keepGoing)
    {
        EnsureStars();
        while (keepGoing())
        {
            var s = UnityEngine.Random.value < 0.4f ? plusStar : UnityEngine.Random.value < 0.6f ? crossStar : dotStar;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            offset.y = Mathf.Abs(offset.y) * 0.8f + 0.2f;                            // mostly around and above her
            StartCoroutine(Twinkle(s, centre.position + (Vector3)offset));
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.04f, 0.12f));
        }
    }

    IEnumerator Twinkle(Sprite s, Vector3 at)
    {
        float size = UnityEngine.Random.Range(0.35f, 0.7f);
        var go = Spawn(s, at, size, out var sr);
        float life = UnityEngine.Random.Range(0.3f, 0.6f);
        var drift = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(0.1f, 0.5f), 0f);
        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            float k = t / life;
            go.transform.position = at + drift * k;
            SetAlpha(sr, Mathf.Sin(k * Mathf.PI));                                   // fades in and out
            yield return null;
        }
        Destroy(go);
    }

    void EnsureStars()
    {
        if (plusStar != null) return;
        plusStar = StarSprite(new[] { "...#...", "...#...", "...#...", "#######", "...#...", "...#...", "...#..." });
        crossStar = StarSprite(new[] { "#...#", ".#.#.", "..#..", ".#.#.", "#...#" });
        dotStar = StarSprite(new[] { ".#.", "###", ".#." });
    }

    static Sprite StarSprite(string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, rows[y][x] == '#' ? Color.white : Color.clear);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
    }

    // A beam of ice slams down onto `impact` from above the screen, holds for a moment, then fades.
    public IEnumerator Column(Vector3 impact, float width, float height)
    {
        var tex = new Texture2D(24, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float edge = 9f + Mathf.PerlinNoise(y * 0.35f, 3.7f) * 3f;               // ragged sides
            for (int x = 0; x < 24; x++)
            {
                float d = Mathf.Abs(x - 11.5f);
                if (d > edge) { tex.SetPixel(x, y, Color.clear); continue; }
                float core = 1f - d / edge;                                          // white in the middle, blue at the edges
                var c = Color.Lerp(new Color(0.35f, 0.65f, 1f, 0.85f), new Color(0.9f, 0.97f, 1f, 0.95f), core * core);
                if ((x * 7 + y * 3) % 11 == 0) c = Color.white;                       // glints
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();

        var go = new GameObject("Noelle ice column");
        spawned.Add(go);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 24, 64), new Vector2(0.5f, 0f), 16f);   // pivot at the bottom
        if (own != null)
        {
            sr.sortingLayerID = own.sortingLayerID;
            sr.sortingOrder = own.sortingOrder + 9;
        }
        go.transform.localScale = new Vector3(width / (24f / 16f), height / (64f / 16f), 1f);

        const float fall = 0.22f;
        for (float t = 0f; t < fall; t += Time.deltaTime)                            // drops fast
        {
            float k = t / fall;
            go.transform.position = new Vector3(impact.x, Mathf.Lerp(impact.y + height, impact.y, k * k), 0f);
            yield return null;
        }
        go.transform.position = new Vector3(impact.x, impact.y, 0f);
        StartCoroutine(FadeAndDestroy(go, sr, 0.7f));                                // the caller carries on at the moment of impact
    }

    IEnumerator FadeAndDestroy(GameObject go, SpriteRenderer sr, float seconds)
    {
        yield return new WaitForSeconds(0.15f);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            SetAlpha(sr, 1f - t / seconds);
            yield return null;
        }
        Destroy(go);
    }

    // The snowstorm: a dark blue tint over the whole screen with two layers of white dots (the sheet's big and small dots)
    // falling at different speeds. Fades in, runs while keepGoing() is true, fades out. It does not block any input.
    public IEnumerator Storm(Func<bool> keepGoing)
    {
        if (snowA == null && snowB == null) yield break;

        var root = new GameObject("Noelle snowstorm");
        spawned.Add(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;                                                    // above the world, under the score
        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var tint = NewLayer(root.transform, "Tint");
        var gradient = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++)                                                 // darker and bluer towards the bottom
            gradient.SetPixel(0, y, Color.Lerp(new Color(0.05f, 0.08f, 0.3f, 0.75f), new Color(0.12f, 0.2f, 0.6f, 0.4f), y / 31f));
        gradient.Apply();
        tint.texture = gradient;

        RawImage small = null, big = null;
        if (snowA != null) { small = NewLayer(root.transform, "Small snow"); small.texture = snowA.texture; snowA.texture.wrapMode = TextureWrapMode.Repeat; }
        if (snowB != null) { big = NewLayer(root.transform, "Big snow"); big.texture = snowB.texture; snowB.texture.wrapMode = TextureWrapMode.Repeat; }
        float smallTile = snowA != null ? snowA.rect.width * 3.5f : 1f;               // screen pixels one picture covers
        float bigTile = snowB != null ? snowB.rect.width * 5f : 1f;

        float time = 0f;
        float alpha = 0f;
        bool fadingOut = false;
        while (alpha > 0f || !fadingOut)
        {
            time += Time.deltaTime;
            if (!fadingOut && !keepGoing()) fadingOut = true;
            alpha = Mathf.MoveTowards(alpha, fadingOut ? 0f : 1f, Time.deltaTime / (fadingOut ? 0.8f : 0.4f));
            group.alpha = alpha;

            if (small != null)
                small.uvRect = new Rect(Mathf.Sin(time * 0.7f) * 0.1f, time * 0.35f, Screen.width / smallTile, Screen.height / smallTile);
            if (big != null)
                big.uvRect = new Rect(Mathf.Sin(time * 0.5f + 1f) * 0.15f + 0.37f, time * 0.55f, Screen.width / bigTile, Screen.height / bigTile);
            yield return null;
        }
        Destroy(root);
    }

    static UnityEngine.UI.RawImage NewLayer(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.AddComponent<UnityEngine.UI.RawImage>();
        image.raycastTarget = false;
        return image;
    }

    // ---- the little animations ----

    IEnumerator Grow(Sprite s, Vector3 at, float fromSize, float toSize, float seconds, float alpha)
    {
        var go = Spawn(s, at, fromSize, out var sr);
        float baseScale = go.transform.localScale.x / fromSize;               // world size 1 = this scale
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            go.transform.localScale = Vector3.one * baseScale * Mathf.Lerp(fromSize, toSize, 1f - (1f - k) * (1f - k));
            SetAlpha(sr, alpha * (1f - k));
            yield return null;
        }
        Destroy(go);
    }

    IEnumerator Pop(Vector3 at)
    {
        var go = Spawn(snowflake, at, 0.4f, out var sr);
        float baseScale = go.transform.localScale.x / 0.4f;
        const float seconds = 0.9f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            float size = Mathf.Lerp(0.4f, 1.1f, Mathf.Sin(Mathf.Min(1f, k * 3f) * Mathf.PI / 2f));   // quick pop, then holds
            go.transform.localScale = Vector3.one * baseScale * size;
            go.transform.rotation = Quaternion.Euler(0f, 0f, -90f * k);
            go.transform.position = at + Vector3.up * (0.6f * k);
            SetAlpha(sr, k < 0.5f ? 1f : 1f - (k - 0.5f) * 2f);
            yield return null;
        }
        Destroy(go);
    }

    IEnumerator Fall(Sprite s, Vector3 from)
    {
        var go = Spawn(s, from, 3.5f, out var sr);
        const float seconds = 1.3f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            go.transform.position = from + Vector3.down * (1.6f * k);
            SetAlpha(sr, Mathf.Sin(k * Mathf.PI) * 0.85f);                  // fades in and out
            yield return null;
        }
        Destroy(go);
    }

    // ---- helpers ----

    // A sprite object whose width is worldWidth units, drawn above the characters.
    GameObject Spawn(Sprite s, Vector3 at, float worldWidth, out SpriteRenderer sr)
    {
        var go = new GameObject("Noelle effect");
        spawned.RemoveAll(o => o == null);
        spawned.Add(go);
        go.transform.position = new Vector3(at.x, at.y, 0f);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        if (own != null)
        {
            sr.sortingLayerID = own.sortingLayerID;
            sr.sortingOrder = own.sortingOrder + 10;
        }
        float width = s.bounds.size.x;
        go.transform.localScale = Vector3.one * (width > 0f ? worldWidth / width : 1f);
        return go;
    }

    static void SetAlpha(SpriteRenderer sr, float a)
    {
        if (sr == null) return;
        var c = sr.color;
        c.a = Mathf.Clamp01(a);
        sr.color = c;
    }
}
