using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Tools > Space Out Obstacles
// The old level had its obstacles close together (two pools 12 apart, a pool and a box 11 apart ...), so two of them were on the
// screen at the same time, which is confusing with one flashing pad that stands for "activate power" (the game picks the
// friend from the obstacle in front of Kris). This inserts stretches of flat ground, so that between two obstacles there are at
// least 29 units (the camera shows about 25 to 27) and only one is on the screen at a time:
//
//   at x = 32 ... 17 columns   (between the two pools)
//   at x = 48 ... 19 columns   (between the second pool and the box)
//   at x = 68 ... 11 columns   (between the box's hole and the tree)
//   at x = 140 .. 18 columns   (between the two spike pits at the end)
//
// Everything to the right of a cut moves to the right by that many columns: all the tiles of the level's tilemaps (the ground, the
// water, the trees, the decorations ...) and the objects on them (gems, spikes, the box, Ralsei's platform, the level goal).
// The new columns are copies of the flat ground next to the cut. Each cut is checked first (flat ground of the same height on both
// sides, no water, no tree or object across the cut) and nothing is changed if one fails. A marker object "Level Spacing (done)"
// stops the tool from running twice. The BCI section is rebuilt afterwards (it starts further right now).
public static class LevelSpacer
{
    const string MarkerName = "Level Spacing (done)";
    const int Bottom = -17;

    // x of the cut and how many columns to insert there. Highest x first: a cut never moves the ones to its left.
    static readonly int[,] Cuts = { { 140, 18 }, { 68, 11 }, { 48, 19 }, { 32, 17 } };

    static Tilemap ground, water;
    static Tilemap[] all;

    [MenuItem("Tools/Space Out Obstacles")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Stop Play mode first."); return; }
        if (GameObject.Find(MarkerName) != null)
        {
            Debug.Log("Space Out Obstacles: already done in this scene (to change it, delete the object '" + MarkerName + "' after restoring the level).");
            return;
        }
        if (!FindTilemaps()) return;

        // ---- check every cut before touching anything ----
        for (int i = 0; i < Cuts.GetLength(0); i++)
            if (!CheckCut(Cuts[i, 0], Cuts[i, 1])) return;

        // ---- do them ----
        int total = 0;
        for (int i = 0; i < Cuts.GetLength(0); i++)
        {
            int x = Cuts[i, 0], add = Cuts[i, 1];
            foreach (var tm in all) Shift(tm, x, add);
            Fill(x, add);
            MoveObjects(x, add);
            total += add;
            Debug.Log($"Space Out Obstacles: {add} columns of ground inserted at x = {x}.");
        }
        foreach (var tm in all) tm.RefreshAllTiles();

        var marker = new GameObject(MarkerName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, ground.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
        Debug.Log($"Space Out Obstacles: done, everything after the first cut moved right ({total} columns in all). Rebuilding the BCI section.");

        BciSectionBuilder.Build();                                                 // saves the scene
    }

    static bool FindTilemaps()
    {
        Tilemap Find(string name)
        {
            foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
                if (tm.name == name && !PrefabUtility.IsPartOfPrefabInstance(tm)) return tm;
            return null;
        }
        ground = Find("Tilemap_Ground");
        water = Find("Tilemap_Water");
        var list = new List<Tilemap>();
        foreach (var name in new[] { "Tilemap_Ground", "Tilemap_Water", "Tilemap_Ice", "Tilemap_Trunk", "Tilemap_Leaves_b", "Tilemap_Leaves_f", "Tilemap_Decorations", "points" })
        {
            var tm = Find(name);
            if (tm != null) list.Add(tm);
        }
        all = list.ToArray();
        if (ground == null || water == null)
        {
            Debug.LogError("Space Out Obstacles: open GameScene first (Tilemap_Ground / Tilemap_Water not found).");
            return false;
        }
        return true;
    }

    // ---- checks ----

    static int TopRow(int x)
    {
        for (int y = ground.cellBounds.yMax; y >= Bottom; y--)
            if (ground.HasTile(new Vector3Int(x, y, 0))) return y;
        return int.MinValue;
    }

    static bool CheckCut(int x, int add)
    {
        int left = TopRow(x - 1), right = TopRow(x);
        if (left == int.MinValue || left != right)
        {
            Debug.LogError($"Space Out Obstacles: the ground at the cut x = {x} is not flat (top rows {left} and {right}). Nothing was changed.");
            return false;
        }
        foreach (var tm in all)
        {
            if (tm == ground) continue;
            for (int y = tm.cellBounds.yMin; y < tm.cellBounds.yMax; y++)
            {
                bool l = tm.HasTile(new Vector3Int(x - 1, y, 0)), r = tm.HasTile(new Vector3Int(x, y, 0));
                if (tm == water ? (l || r) : (l && r))
                {
                    Debug.LogError($"Space Out Obstacles: '{tm.name}' crosses the cut x = {x} (row {y}). Nothing was changed.");
                    return false;
                }
            }
        }
        // an object (the box, a spike zone, Ralsei ...) with a collider across the cut would be torn apart
        foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c is CompositeCollider2D || c.GetComponent<Tilemap>() != null || c.GetComponent<TilemapCollider2D>() != null) continue;
            if (c.GetComponentInParent<PlayerController>() != null) continue;
            var b = c.bounds;
            if (b.size.x > 0.01f && b.min.x < x && b.max.x > x && b.max.y > -20f)
            {
                Debug.LogError($"Space Out Obstacles: '{c.name}' stands across the cut x = {x} (x {b.min.x:0.0} to {b.max.x:0.0}). Nothing was changed.");
                return false;
            }
        }
        return true;
    }

    // ---- doing it ----

    // Every tile with x >= cut moves `add` columns to the right (right-most column first, so nothing is written over).
    static void Shift(Tilemap tm, int cut, int add)
    {
        var b = tm.cellBounds;
        for (int x = b.xMax - 1; x >= cut; x--)
        {
            for (int y = b.yMin; y < b.yMax; y++)
            {
                var from = new Vector3Int(x, y, 0);
                var tile = tm.GetTile(from);
                if (tile == null) continue;
                var to = new Vector3Int(x + add, y, 0);
                var matrix = tm.GetTransformMatrix(from);
                var colour = tm.GetColor(from);
                var flags = tm.GetTileFlags(from);
                tm.SetTile(to, tile);
                tm.SetTransformMatrix(to, matrix);
                tm.SetTileFlags(to, TileFlags.None);
                tm.SetColor(to, colour);
                tm.SetTileFlags(to, flags);
                tm.SetTile(from, null);
            }
        }
    }

    // The new columns: copies of the ground column just left of the cut.
    static void Fill(int cut, int add)
    {
        for (int dx = 0; dx < add; dx++)
            for (int y = Bottom; y <= ground.cellBounds.yMax; y++)
                ground.SetTile(new Vector3Int(cut + dx, y, 0), ground.GetTile(new Vector3Int(cut - 1, y, 0)));
    }

    // The objects of the level that stand to the right of the cut move with the tiles: gems, spikes, boxes, the duplicate tree,
    // Ralsei's platform and the level goal. (The BCI section is rebuilt anyway.)
    static void MoveObjects(int cut, int add)
    {
        var shift = new Vector3(add, 0f, 0f);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool mine = t.name == "Ralsei_Hook_Platform"
                        || (t.parent == null && (t.GetComponent<ScorePoint>() != null || t.name.StartsWith("Spike") || t.GetComponent<PushableObject>() != null
                                                 || t.GetComponent<LevelGoal>() != null || t.name == "Obs_tree"));
            if (!mine || t.position.x < cut) continue;
            Undo.RecordObject(t, "Space out obstacles");
            t.position += shift;
        }
    }
}
