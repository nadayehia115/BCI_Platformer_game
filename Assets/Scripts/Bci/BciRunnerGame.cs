using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The mini-game of the loading screen: a tiny endless runner. Kris (the running and jumping frames of the game itself) runs forward by himself on the game's own ground tiles; small crates
// (a tile of the game's tileset) come towards him and he jumps over them with Space, W, the Up arrow or a mouse click. The crates are SOLID,
// like in the real game: Kris cannot walk into one - the world stops against it until he jumps - and he can land on top of a crate and run
// along it. Nothing flashes and nothing vanishes: it is a warm-up, not a test. Everything is UI images moved by code (it needs no scene and
// no physics), and it runs on real time. Freeze() stops it when the model is ready.
public class BciRunnerGame : MonoBehaviour
{
    const float TileSize = 72f;                 // a tile of the tileset (18 px) at 4x
    const float Speed = 430f;                   // how fast the world moves left (canvas units per second)
    const float JumpSpeed = 1180f, Gravity = 3300f;
    const float KrisX = -560f;
    const float KrisScale = 5.5f;               // his 45-55 px pictures at 5.5x
    const float KrisFront = KrisX + 80f;        // his hit box, sideways: from his back ...
    const float KrisBack = KrisX - 50f;         // ... to his front (canvas units)
    const float CrateTop = TileSize;            // a crate is one tile high
    const float JumpBufferSeconds = 0.14f;      // a jump pressed this long before landing still happens

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
    int jumps;
    float jumpBuffer;

    class Crate { public RectTransform rect; }
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

        // what is under Kris: the ground, or the top of a crate he is above (he lands on it and runs along it)
        float floor = 0f;
        foreach (var c in crates)
        {
            float left = c.rect.anchoredPosition.x - TileSize * 0.5f, right = left + TileSize;
            if (right > KrisBack && left < KrisFront && height >= CrateTop - 10f) floor = CrateTop;
        }

        // jump (Space, W, Up, a click); a press just before landing still counts
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool pressed = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) ||
                       (mouse != null && mouse.leftButton.wasPressedThisFrame);
        jumpBuffer = pressed ? JumpBufferSeconds : Mathf.Max(0f, jumpBuffer - dt);
        bool grounded = height <= floor + 0.01f && vy <= 0f;
        if (grounded && jumpBuffer > 0f)
        {
            vy = JumpSpeed;
            jumps++;
            jumpBuffer = 0f;
            grounded = false;
        }
        if (!grounded)
        {
            vy -= Gravity * dt;
            height += vy * dt;
            if (height <= floor && vy <= 0f) { height = floor; vy = 0f; }
        }
        else height = floor;                                       // (stepping onto a crate, or standing on the ground)

        // the world moves left - but not into a crate that Kris has not jumped onto: it stops right at his front
        float scroll = Speed * dt;
        if (height < CrateTop - 10f)
            foreach (var c in crates)
            {
                float left = c.rect.anchoredPosition.x - TileSize * 0.5f, right = left + TileSize;
                float gap = left - KrisFront;                      // free space between his front and the crate
                if (right > KrisBack && gap > -TileSize && gap < scroll) scroll = Mathf.Max(0f, gap);
            }
        groundOffset = (groundOffset + scroll) % TileSize;
        PlaceGround();
        PlaceKris();

        // Kris: the running frames on the ground (or on a crate), the jumping frames in the air (going up / top / coming down)
        bool inAir = height > floor + 1f;
        if (inAir && jumpFrames.Length > 0)
        {
            int index = vy > 500f ? 0 : vy < -500f ? Mathf.Min(2, jumpFrames.Length - 1) : Mathf.Min(1, jumpFrames.Length - 1);
            ShowKris(jumpFrames[index]);
        }
        else if (runFrames.Length > 0)
            ShowKris(runFrames[(int)(Time.unscaledTime * 9f) % runFrames.Length]);

        // crates (they move with the world, so they wait too while Kris is held up)
        nextBox -= scroll / Speed;
        if (nextBox <= 0f)
        {
            SpawnCrate();
            nextBox = Random.Range(1.5f, 2.6f);
        }
        for (int i = crates.Count - 1; i >= 0; i--)
        {
            var c = crates[i];
            c.rect.anchoredPosition += new Vector2(-scroll, 0f);
            if (c.rect.anchoredPosition.x < -960f - TileSize)
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
