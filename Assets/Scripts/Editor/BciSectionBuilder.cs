using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Tools > Build BCI Obstacle Section
// Adds the second half of the level after the old end (from StartX), built from the level's own tilemaps, so it uses the
// tiles of the game's tile palette: the grass rule tile for the ground, the water tiles of the existing pools, the tree of the
// Obs_tree prefab, the Pushable_Pillar prefab for the box. In the order the player meets them:
//
//   water pool 14 wide ......... Noelle        (she freezes it, Kris walks over the ice)
//   spike pit 4 wide ........... Kris alone    (a jump)                      <- rest from the BCI
//   spike pit 13 wide + tree ... Ralsei        (he hangs from the branch, Kris stands on him: needed, one jump is not enough; the
//                                                ground after the pit is 1 higher, each of Kris's two hops is short)
//   block 2 high ............... Kris alone    (a jump)                      <- rest from the BCI
//   box + hole + 4 high cliff .. Susie         (she pushes the box into the hole, Kris climbs on it to reach the cliff)
//   flag (and the Level Goal)   moved to the end of the new part
//
// Three score gems follow every obstacle. Everything that is not a tile is a child of "BCI Obstacle Section". Running the tool
// again clears the section and builds it anew, so the numbers below can be changed and the tool run again.
public static class BciSectionBuilder
{
    const string RootName = "BCI Obstacle Section";
    const string TreePath = "Assets/prefabs/Obs_tree.prefab";
    const string GemPath = "Assets/prefabs/score_points.prefab";

    // ---- the layout (cell columns; the surface of the old end of the level is row 1, so the ground is at y = 2) ----
    // There are at least 29 units of flat ground between two obstacles (the camera shows about 25 to 27), so only one obstacle is
    // on the screen at a time. Tools > Space Out Obstacles did the same for the old level and moved its end from 171 to 236.
    const int StartX = 237;                       // the first column after the old end of the level (the old flag stood at 235)
    const int Bottom = -17;                       // lowest ground row of the level
    const int Surface = 1;                        // top ground row
    const int Step = 1;                           // the ground after the tree pit is this much higher: a jump straight over the pit is then out
                                                  // of the question (the most he gets up there is about 10.5 across), but from Ralsei's platform,
                                                  // which is 0.7 above it, it is a short hop
    const int After = Surface + Step;             // top ground row after the tree pit (the block, the box and the cliff stand on it)

    const int PoolX = 247, PoolWidth = 14;        // water: wider than Kris can jump (he jumps about 11), so he needs Noelle
    const int Pit1X = 290, Pit1Width = 4;         // spike pit he jumps by himself
    const int TreeX = 323;                        // the trunk of the tree stands on the last ground column before the pit
    const int Pit2X = 324, Pit2Width = 13;        // spike pit: too wide for one jump (his best is about 12), but each half is easy: 3.8 to Ralsei's platform, 6.7 from it
    const int WallX = 366, WallWidth = 3, WallHeight = 2;   // a block he jumps onto and over by himself
    const int PillarX = 400;                      // the box stands here (6 columns before the hole, like the old one)
    const int HoleX = 406, HoleWidth = 3, HoleDepth = 3;    // the hole the box goes into (the box is 2.5 wide and 5 tall)
    const int CliffX = 409, CliffHeight = 4;      // the ground behind the hole is 4 higher: only from the box can Kris get up
    const int EndX = 429;                         // last column of the level
    const int FlagX = 425;
    const int PitFloor = -6;                      // floor row of the spike pits (as in the old pits)

    // columns of the old level that are copied (left of every cut of the spacing tool, so they never move)
    const int PoolSourceX = 20;                   // an inside column of the first pool: water rows and the bed under them
    const int PoolSourceTop = -3;                 // its surface row
    const int FlatSourceX = 10, FlatSourceY = -5; // a plain ground cell: the grass rule tile
    const float TimeLimit = 360f;                 // the level is longer now: seconds Kris has

    static Tilemap ground, water, ice, trunk, leavesB, leavesF, deco, points;
    static TileBase grass;

    [MenuItem("Tools/Build BCI Obstacle Section")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("Stop Play mode first."); return; }
        if (!FindTilemaps()) return;

        grass = ground.GetTile(new Vector3Int(FlatSourceX, FlatSourceY, 0));
        var poolSurface = water.GetTile(new Vector3Int(PoolSourceX, PoolSourceTop, 0));
        var poolBody = water.GetTile(new Vector3Int(PoolSourceX, PoolSourceTop - 1, 0));
        if (grass == null || poolSurface == null || poolBody == null)
        {
            Debug.LogError("BCI section: the old level is not where it was (no ground at 160,-5 or no water at 20,-3).");
            return;
        }

        // the flag: the right-most column of Tilemap_Decorations (two tiles). Taken first, so the section can be rebuilt.
        if (!TakeFlag(out TileBase flagLower, out TileBase flagUpper)) return;

        ClearSection();

        // ---- ground ----
        PaintGround(StartX, PoolX - 1, Surface);
        PaintPool(PoolX, PoolWidth);
        PaintGround(PoolX + PoolWidth, Pit1X - 1, Surface);
        PaintPit(Pit1X, Pit1Width, PitFloor);
        PaintGround(Pit1X + Pit1Width, Pit2X - 1, Surface);                    // includes the tree's column
        PaintPit(Pit2X, Pit2Width, PitFloor);
        PaintGround(Pit2X + Pit2Width, WallX - 1, After);
        PaintGround(WallX, WallX + WallWidth - 1, After + WallHeight);
        PaintGround(WallX + WallWidth, HoleX - 1, After);
        PaintPit(HoleX, HoleWidth, After - HoleDepth);
        PaintGround(CliffX, EndX, After + CliffHeight);
        int cliffTop = After + CliffHeight;

        // ---- the tree ----
        if (!PlantTree()) return;

        // ---- the flag, at the end ----
        deco.SetTile(new Vector3Int(FlagX, cliffTop + 1, 0), flagLower);
        deco.SetTile(new Vector3Int(FlagX, cliffTop + 2, 0), flagUpper);
        var goal = Object.FindFirstObjectByType<LevelGoal>();
        if (goal != null)
        {
            Undo.RecordObject(goal.transform, "Move level goal");
            goal.transform.position = new Vector3(FlagX + 0.5f, cliffTop + 1.5f, 0f);
            var so = new SerializedObject(goal);
            so.FindProperty("timeLimit").floatValue = TimeLimit;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else Debug.LogWarning("BCI section: no Level Goal in the scene, the flag is only the decoration.");

        // ---- objects: the box, the spikes, the gems ----
        var root = new GameObject(RootName);
        SceneManager_Move(root);
        AddPillar(root.transform);
        AddSpikes(root.transform, Pit1X, Pit1Width, 1);
        AddSpikes(root.transform, Pit2X, Pit2Width, 2);
        AddGems(root.transform);

        ground.RefreshAllTiles();
        water.RefreshAllTiles();

        // Ralsei hangs from the nearest tree the player can see: a hook point on every tree, the old one and the new one
        ConnectRalsei();

        EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
        EditorSceneManager.SaveScene(ground.gameObject.scene);
        Debug.Log($"BCI section built: x {StartX} to {EndX}, flag at {FlagX}, time limit {TimeLimit} s. The scene is saved.");
    }

    // ---- finding things ----

    static bool FindTilemaps()
    {
        Tilemap Find(string name)
        {
            foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
                if (tm.name == name && !PrefabUtility.IsPartOfPrefabInstance(tm)) return tm;
            return null;
        }
        ground = Find("Tilemap_Ground"); water = Find("Tilemap_Water"); ice = Find("Tilemap_Ice");
        trunk = Find("Tilemap_Trunk"); leavesB = Find("Tilemap_Leaves_b"); leavesF = Find("Tilemap_Leaves_f");
        deco = Find("Tilemap_Decorations"); points = Find("points");
        if (ground == null || water == null || ice == null || trunk == null || leavesB == null || leavesF == null || deco == null)
        {
            Debug.LogError("BCI section: open GameScene first (a tilemap of the level was not found).");
            return false;
        }
        return true;
    }

    static void SceneManager_Move(GameObject go) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, ground.gameObject.scene);

    // The flag = the two tiles of the right-most column of the decorations.
    static bool TakeFlag(out TileBase lower, out TileBase upper)
    {
        lower = upper = null;
        int maxX = int.MinValue;
        foreach (var cell in deco.cellBounds.allPositionsWithin)
            if (deco.HasTile(cell)) maxX = Mathf.Max(maxX, cell.x);
        var column = new List<Vector3Int>();
        foreach (var cell in deco.cellBounds.allPositionsWithin)
            if (deco.HasTile(cell) && cell.x == maxX) column.Add(cell);
        column.Sort((a, b) => a.y.CompareTo(b.y));
        if (column.Count != 2)
        {
            Debug.LogError($"BCI section: expected the flag (2 tiles) at the right end of Tilemap_Decorations, found {column.Count} at x {maxX}.");
            return false;
        }
        lower = deco.GetTile(column[0]);
        upper = deco.GetTile(column[1]);
        foreach (var cell in column) deco.SetTile(cell, null);
        return true;
    }

    // ---- clearing ----

    static void ClearSection()
    {
        foreach (var tm in new[] { ground, water, ice, trunk, leavesB, leavesF, deco, points })
        {
            if (tm == null) continue;
            var doomed = new List<Vector3Int>();
            foreach (var cell in tm.cellBounds.allPositionsWithin)
                if (cell.x >= StartX && tm.HasTile(cell)) doomed.Add(cell);
            foreach (var cell in doomed) tm.SetTile(cell, null);
        }
        var old = GameObject.Find(RootName);
        while (old != null)
        {
            Object.DestroyImmediate(old);
            old = GameObject.Find(RootName);
        }
    }

    // ---- painting ----

    // Ground from the bottom up to row `top`, columns x0..x1: the grass rule tile, which makes the edges and corners itself.
    static void PaintGround(int x0, int x1, int top)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = Bottom; y <= top; y++)
                ground.SetTile(new Vector3Int(x, y, 0), grass);
    }

    // A pit: ground only up to the floor row.
    static void PaintPit(int x0, int width, int floorRow) => PaintGround(x0, x0 + width - 1, floorRow);

    // A pool: every column is a copy of an inside column of the first pool (3 rows of water, the bed under it), moved up or down
    // so that the surface is level with the ground.
    static void PaintPool(int x0, int width)
    {
        int dy = Surface - PoolSourceTop;
        for (int x = x0; x < x0 + width; x++)
        {
            for (int y = Bottom; y <= Surface; y++)
            {
                int sy = Mathf.Max(y - dy, Bottom);
                var src = new Vector3Int(PoolSourceX, sy, 0);
                var dst = new Vector3Int(x, y, 0);
                ground.SetTile(dst, ground.GetTile(src));
                water.SetTile(dst, water.GetTile(src));
            }
        }
    }

    // ---- the tree: the tiles of the Obs_tree prefab planted into the level's tilemaps ----

    static bool PlantTree()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreePath);
        if (prefab == null) { Debug.LogError("BCI section: missing " + TreePath); return false; }
        Tilemap Child(string name) => prefab.GetComponentsInChildren<Tilemap>(true).FirstOrDefault(t => t.gameObject.name == name);
        var srcTrunk = Child("trunk");
        if (srcTrunk == null) { Debug.LogError("BCI section: the Obs_tree prefab has no 'trunk' tilemap."); return false; }

        // its trunk: the column with the most tiles, from its lowest tile up. That column goes on the last ground column
        // before the pit, its lowest tile on the first row above the ground.
        var perColumn = new Dictionary<int, int>();
        var lowest = new Dictionary<int, int>();
        foreach (var cell in srcTrunk.cellBounds.allPositionsWithin)
        {
            if (!srcTrunk.HasTile(cell)) continue;
            perColumn[cell.x] = perColumn.TryGetValue(cell.x, out int n) ? n + 1 : 1;
            lowest[cell.x] = lowest.TryGetValue(cell.x, out int y) ? Mathf.Min(y, cell.y) : cell.y;
        }
        int trunkColumn = perColumn.OrderByDescending(kv => kv.Value).First().Key;
        var shift = new Vector3Int(TreeX - trunkColumn, Surface + 1 - lowest[trunkColumn], 0);

        var pairs = new (string source, Tilemap target)[] { ("trunk", trunk), ("lb", leavesB), ("lf", leavesF), ("hang", deco) };
        int copied = 0;
        foreach (var (source, target) in pairs)
        {
            var src = Child(source);
            if (src == null) { Debug.LogWarning($"BCI section: the Obs_tree prefab has no '{source}' tilemap."); continue; }
            foreach (var cell in src.cellBounds.allPositionsWithin)
            {
                var tile = src.GetTile(cell);
                if (tile == null) continue;
                target.SetTile(cell + shift, tile);
                copied++;
            }
        }
        Debug.Log($"BCI section: planted the tree ({copied} tiles, shifted by {shift}).");
        return true;
    }

    static void ConnectRalsei()
    {
        var platform = GameObject.Find("Ralsei_Hook_Platform");
        if (platform == null || platform.transform.Find("Scarf") == null)
        {
            Debug.LogWarning("BCI section: no Ralsei_Hook_Platform in the scene, so the new tree has no hook point (run Tools > Add Hook Points To Trees later).");
            return;
        }
        HookPlatformSetup.AddHookPoints();
    }

    // ---- objects ----

    // The box: a copy of the old Pushable_Pillar instance (so it has the same overrides), standing on the ground 6 columns before
    // its hole like the old one. It drops onto the ground when the game starts.
    static void AddPillar(Transform parent)
    {
        GameObject source = null;
        foreach (var pushable in Object.FindObjectsByType<PushableObject>(FindObjectsSortMode.None))
            if (!pushable.transform.IsChildOf(parent) && pushable.transform.parent == null && pushable.name == "Pushable_Pillar") source = pushable.gameObject;
        if (source == null)
        {
            Debug.LogWarning("BCI section: no Pushable_Pillar in the scene to copy, so the box was not added.");
            return;
        }
        var box = (GameObject)Object.Instantiate(source, parent);
        box.name = "Pushable_Pillar (BCI box)";
        // the collider is 5 high and starts 2.5 below its middle: the bottom is on the ground (surface y = After + 1) plus a little to drop
        var collider = box.GetComponent<BoxCollider2D>();
        float centreAboveRoot = collider != null ? collider.offset.y : 0f;
        float half = collider != null ? collider.size.y / 2f : 2.5f;
        box.transform.position = new Vector3(PillarX, After + 1 + half - centreAboveRoot + 0.15f, 0f);
    }

    // The spikes along the floor of a pit. The old level has two kinds of Spike_Pit_Zone (the spikes are drawn by its children, the
    // zone that costs a life is its collider): the plain prefab, which fits a pit 6 wide, and "Spike_Pit_Zone (1)", trimmed to a pit
    // 4 wide. A wider pit is covered by several of them side by side (14 = 6 + 4 + 4). An object stands 3.37 to the right of the
    // left edge of the pit it covers, like the old ones.
    static void AddSpikes(Transform parent, int x0, int width, int number)
    {
        GameObject six = null, four = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.parent != null || t.IsChildOf(parent)) continue;
            if (t.name == "Spike_Pit_Zone") six = t.gameObject;
            else if (t.name == "Spike_Pit_Zone (1)") four = t.gameObject;
        }
        if (six == null || four == null)
        {
            Debug.LogWarning("BCI section: the scene has no Spike_Pit_Zone and Spike_Pit_Zone (1) to copy, so the pit has no spikes.");
            return;
        }

        var pieces = new List<int>();
        for (int left = width; left > 0;)
        {
            int piece = left == 6 || left > 8 ? 6 : 4;                 // 14 -> 6, 4, 4     4 -> 4     6 -> 6
            pieces.Add(piece);
            left -= piece;
        }
        int x = x0, index = 0;
        foreach (int piece in pieces)
        {
            var source = piece == 6 ? six : four;
            var spikes = (GameObject)Object.Instantiate(source, parent);
            spikes.name = "Spike_Pit_Zone (BCI " + number + "." + (++index) + ")";
            spikes.transform.position = new Vector3(x + 3.37f, source.transform.position.y, 0f);
            x += piece;
        }
    }

    // Three gems after every obstacle (and one above the block, for the jump).
    static void AddGems(Transform parent)
    {
        var gem = AssetDatabase.LoadAssetAtPath<GameObject>(GemPath);
        if (gem == null) { Debug.LogWarning("BCI section: missing " + GemPath + ", so there are no gems."); return; }

        float y = Surface + 1 + 1.3f;                        // 1.3 above the ground
        float yAfter = After + 1 + 1.3f;                     // the same, after the tree pit
        float cliffY = After + CliffHeight + 1 + 1.3f;
        var spots = new List<Vector2>();
        void Row(float x, float height, int count = 3) { for (int i = 0; i < count; i++) spots.Add(new Vector2(x + i * 1.6f, height)); }

        Row(PoolX + PoolWidth + 2.5f, y);                    // after the pool
        Row(Pit1X + Pit1Width + 2.5f, y);                    // after the first pit
        Row(Pit2X + Pit2Width + 2.5f, yAfter);               // after the tree pit
        spots.Add(new Vector2(WallX + WallWidth / 2f, After + WallHeight + 1 + 1.3f));      // above the block
        Row(WallX + WallWidth + 2f, yAfter, 2);              // after the block
        Row(CliffX + 2.5f, cliffY);                          // on the cliff, after the box

        int n = 0;
        foreach (var spot in spots)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(gem, parent);
            go.name = "score_points (BCI " + (++n) + ")";
            go.transform.position = new Vector3(spot.x, spot.y, 0f);
        }
    }
}
