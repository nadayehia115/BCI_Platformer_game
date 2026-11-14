using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The mini-game of the loading screen: a tiny endless runner. Kris (the running and jumping frames of the game itself) runs forward by himself on the game's own ground tiles; small crates
// (a tile of the game's tileset) come towards him and he jumps over them with Space, W or the Up arrow. A crate that hits him just pops
// away (nothing flashes) - it is a warm-up, not a test. Everything is UI images moved by code (it needs no scene and no physics), and it runs
// on real time. Freeze() stops it when the model is ready.
public class BciRunnerGame : MonoBehaviour
{
    const float TileSize = 72f;                 // a tile of the tileset (18 px) at 4x
    const float Speed = 430f;                   // how fast the world moves left (canvas units per second)
    const float JumpSpeed = 1180f, Gravity = 3300f;
    const float KrisX = -560f;
    const float KrisScale = 5.5f;               // his 45-55 px pictures at 5.5x

    RectTransform world, kris;
    Image krisImage;
    Sprite[] runFrames, jumpFrames;
    Sprite crateSprite;
    RectTransform[] groundTiles;
    float groundTop;
    float groundOffset;
    float vy, height;
    float nextBox;
    bool frozen;
    int jumps, bonks;

    class Crate { public RectTransform rect; public bool passed; public float pop = -1f; }
    readonly System.Collections.Generic.List<Crate> crates = new System.Collections.Generic.List<Crate>();

    public int Jumps => jumps;

    public static BciRunnerGame Create(Transform parent, float groundTopY)
    {
        var rect = BciUi.Rect("Runner", parent);
        BciUi.Stretch(rect);
        var game = rect.gameObject.AddComponent<BciRunnerGame>();
        game.Build(rect, groundTopY);
        return game;
    }

    void Build(RectTransform root, float groundTopY)
    {
        groundTop = groundTopY;
        world = root;

        var art = Resources.Load<BciArt>("BciArt");
        runFrames = LoadFrames("kris_run_", 3);
        jumpFrames = LoadFrames("kris_jump_", 3);
        crateSprite = art != null ? art.crate : null;

        // the ground: two rows of the game's tiles (grass on top, earth below), one extra column so it can scroll without a gap
        int columns = Mathf.CeilToInt(1920f / TileSize) + 2;
        int rows = Mathf.CeilToInt((groundTopY + 540f) / TileSize);
        groundTiles = new RectTransform[columns * rows];
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                var tile = BciUi.Rect("Ground", root);
                tile.anchorMin = tile.anchorMax = tile.pivot = new Vector2(0.5f, 0.5f);
                tile.sizeDelta = new Vector2(TileSize + 1f, TileSize + 1f);
                var image = tile.gameObject.AddComponent<Image>();
                image.sprite = art != null ? (row == 0 ? art.grass : art.dirt) : null;
                image.color = art != null ? Color.white : (row == 0 ? new Color(0.2f, 0.7f, 0.3f) : new Color(0.55f, 0.35f, 0.2f));
                image.raycastTarget = false;
                groundTiles[row * columns + col] = tile;
            }

        // Kris
        kris = BciUi.Rect("Kris", root);
        kris.pivot = new Vector2(0.5f, 0f);
        kris.anchorMin = kris.anchorMax = new Vector2(0.5f, 0.5f);
        krisImage = kris.gameObject.AddComponent<Image>();
        krisImage.raycastTarget = false;
        if (runFrames.Length > 0) ShowKris(runFrames[0]);
        else kris.sizeDelta = new Vector2(120f, 160f);

        nextBox = 1.2f;
        PlaceGround();
        PlaceKris();
    }

    void PlaceGround()
    {
        int columns = Mathf.CeilToInt(1920f / TileSize) + 2;
        for (int i = 0; i < groundTiles.Length; i++)
        {
            int row = i / columns, col = i % columns;
            float x = -960f + col * TileSize - groundOffset + TileSize * 0.5f;
            groundTiles[i].anchoredPosition = new Vector2(x, groundTop - TileSize * 0.5f - row * TileSize);
        }
    }

    void PlaceKris() => kris.anchoredPosition = new Vector2(KrisX, groundTop + height);

    // Kris's frames (Assets/Resources/Bci/kris_run_N, kris_jump_N): cut from the game's own "Kris run" / "Krisjump" pictures and put on ONE common
    // canvas with the feet on the bottom edge and the body in the middle, so switching between running and jumping never moves or resizes him.
    static Sprite[] LoadFrames(string prefix, int count)
    {
        var list = new System.Collections.Generic.List<Sprite>();
        for (int i = 1; i <= count; i++)
        {
            var sprite = Resources.Load<Sprite>("Bci/" + prefix + i);
            if (sprite != null) list.Add(sprite);
        }
        return list.ToArray();
    }

    void ShowKris(Sprite sprite)
    {
        krisImage.sprite = sprite;
        kris.sizeDelta = sprite.rect.size * KrisScale;
    }

    public void Freeze() => frozen = true;

    void Update()
    {
        if (frozen) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        // the world moves left
        groundOffset = (groundOffset + Speed * dt) % TileSize;
        PlaceGround();

        // jump
        var kb = Keyboard.current;
        bool jumpPressed = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame);
        if (jumpPressed && height <= 0.01f)
        {
            vy = JumpSpeed;
            jumps++;
        }
        if (height > 0f || vy > 0f)
        {
            vy -= Gravity * dt;
            height += vy * dt;
            if (height <= 0f) { height = 0f; vy = 0f; }
        }
        PlaceKris();

        // Kris: the running frames on the ground, the jumping frames in the air (going up / top / coming down)
        bool inAir = height > 0.01f;
        if (inAir && jumpFrames.Length > 0)
        {
            int index = vy > 500f ? 0 : vy < -500f ? Mathf.Min(2, jumpFrames.Length - 1) : Mathf.Min(1, jumpFrames.Length - 1);
            ShowKris(jumpFrames[index]);
        }
        else if (runFrames.Length > 0)
            ShowKris(runFrames[(int)(Time.unscaledTime * 9f) % runFrames.Length]);

        // crates
        nextBox -= dt;
        if (nextBox <= 0f)
        {
            SpawnCrate();
            nextBox = Random.Range(1.5f, 2.6f);
        }
        for (int i = crates.Count - 1; i >= 0; i--)
        {
            var c = crates[i];
            c.rect.anchoredPosition += new Vector2(-Speed * dt, 0f);
            float x = c.rect.anchoredPosition.x;
            // the hit: Kris's feet below the top of the crate while they overlap sideways: the crate pops away
            if (!c.passed && Mathf.Abs(x - KrisX) < TileSize * 0.55f + 28f && height < TileSize * 0.85f)
            {
                bonks++;
                c.passed = true;
                c.pop = 0f;
            }
            else if (!c.passed && x < KrisX - TileSize)
                c.passed = true;
            if (c.pop >= 0f)
            {
                c.pop += dt;
                c.rect.localScale = Vector3.one * Mathf.Max(0f, 1f - c.pop / 0.15f);
            }
            if (x < -960f - TileSize || c.pop > 0.15f)
            {
                Destroy(c.rect.gameObject);
                crates.RemoveAt(i);
            }
        }
    }

    void SpawnCrate()
    {
        var rect = BciUi.Rect("Crate", world);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(TileSize, TileSize);
        rect.anchoredPosition = new Vector2(960f + TileSize, groundTop);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = crateSprite;
        image.color = crateSprite != null ? Color.white : new Color(0.6f, 0.4f, 0.2f);
        image.raycastTarget = false;
        crates.Add(new Crate { rect = rect });
    }
}
