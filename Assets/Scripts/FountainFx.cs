using UnityEngine;

// The shapes of the dark fountain sequence that are not in the pictures from the sheets, drawn in code: the white screen, the
// beam of darkness, the white ellipse on the floor, the disc the smoke puffs are made of, and the flicker dot.
public static class FountainFx
{
    static Sprite white, ellipse, disc, beam, dot;

    // A 1 x 1 unit white square: scale it to cover the screen.
    public static Sprite White => white != null ? white : white = Make(4, 4, 4f, (x, y, w, h) => Color.white);

    // A flat white ellipse, 4 x 1 units: the glow on the floor under Kris.
    public static Sprite Ellipse => ellipse != null ? ellipse : ellipse = Make(64, 16, 16f, (x, y, w, h) =>
    {
        float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
        return dx * dx + dy * dy <= 1f ? Color.white : Color.clear;
    });

    // A plain white disc, 3 x 3 units at scale 1 (tint it black for the smoke, white for its outline).
    public static Sprite Disc => disc != null ? disc : disc = Make(48, 48, 16f, (x, y, w, h) =>
    {
        float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
        return dx * dx + dy * dy <= 1f ? Color.white : Color.clear;
    });

    // The beam of darkness: a column, open at the top and rounded to a point at the bottom (the pivot is the bottom tip), 6 units
    // wide and 12 tall at scale 1. It is white; tint it with the sprite colour.
    public static Sprite Beam => beam != null ? beam : beam = Make(96, 192, 16f, (x, y, w, h) =>
    {
        float t = (y + 0.5f) / h;                                    // 0 at the tip, 1 at the top
        float half = Mathf.Sqrt(t);                                  // half the width at that height, 0..1
        float dx = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
        return dx <= half ? Color.white : Color.clear;
    }, new Vector2(0.5f, 0f));

    // The flicker dot: a white disc with a black outline, 32 px.
    public static Sprite Dot => dot != null ? dot : dot = Make(32, 32, 16f, (x, y, w, h) =>
    {
        float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r > 1f) return Color.clear;
        return r > 0.82f ? Color.black : Color.white;
    });

    delegate Color Pixel(int x, int y, int w, int h);

    static Sprite Make(int w, int h, float ppu, Pixel pixel, Vector2? pivot = null)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, pixel(x, y, w, h));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), pivot ?? new Vector2(0.5f, 0.5f), ppu);
    }
}
