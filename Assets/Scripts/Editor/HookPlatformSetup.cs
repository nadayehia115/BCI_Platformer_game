using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Tools > Hang Ralsei Platform On Tree
// Builds "Ralsei_Hook_Platform" in the open scene: Ralsei hanging from the nearest branch of the trunk tilemap by his scarf
// (R-Action), fixed there as a child of the tree. It has two sprites:
//   Scarf - drawn BEHIND Kris
//   Body  - drawn IN FRONT of Kris (so his feet are hidden while he stands on it) and carries the one-way platform collider
// It becomes the HUD's Ralsei: the CharacterSwitcher shows it when Ralsei is selected and confirmed with Z, and hides it
// again with the next Z. The follower Ralsei under Companions is removed because this replaces it.
// Run Tools > Build Character Animations first (it imports nothing here, but the art comes from the same folder).
public static class HookPlatformSetup
{
    const string PlatformName = "Ralsei_Hook_Platform";
    const string Folder = "Assets/Characters/Ralsei/RAction/";
    const string BodyPath = Folder + "Ralsei_hook_body.png";
    const string ScarfPath = Folder + "Ralsei_hook_scarf.png";
    const int HookPpu = 25;                        // same scale as Ralsei's R-Action frames (16 * 1.55)
    const float KnotCenterBelowTop = 0.55f;        // the scarf knot is about 1.1 units tall; its middle goes round the branch
    const float PlatformThickness = 0.3f;
    const int RalseiSlot = 0;                      // first portrait in the HUD

    [MenuItem("Tools/Hang Ralsei Platform On Tree")]
    internal static void Hang()
    {
        var body = ImportSprite(BodyPath);
        var scarf = ImportSprite(ScarfPath);
        if (body == null || scarf == null)
        {
            EditorUtility.DisplayDialog("Hang Ralsei Platform", "Missing " + BodyPath + " / " + ScarfPath + ". Run python tools/extract_ralsei_raction.py first.", "OK");
            return;
        }

        var trunks = new List<Tilemap>();
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (tm.name.ToLowerInvariant().Contains("trunk") && tm.GetUsedTilesCount() > 0) trunks.Add(tm);
        if (trunks.Count == 0)
        {
            EditorUtility.DisplayDialog("Hang Ralsei Platform", "No tilemap with 'Trunk' in its name found in the open scene. Open GameScene first.", "OK");
            return;
        }

        // ---- the follower Ralsei is replaced by this hanging one; remember where it was put to start from there ----
        Vector3? start = null;
        foreach (var follow in Object.FindObjectsByType<CompanionFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (follow.name != "Ralsei") continue;
            start = follow.transform.position;
            Undo.DestroyObjectImmediate(follow.gameObject);
        }

        // ---- the platform object: an empty root with a Scarf and a Body child ----
        var root = GameObject.Find(PlatformName);
        if (root == null)
        {
            root = new GameObject(PlatformName);
            Undo.RegisterCreatedObjectUndo(root, "Create Ralsei hook platform");
            var view = SceneView.lastActiveSceneView;
            root.transform.position = start ?? (view != null ? (Vector3)(Vector2)view.pivot : Vector3.zero);
        }
        Undo.RecordObject(root.transform, "Hang Ralsei platform");
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
        // leftovers from the first version of this tool, where the root carried everything
        foreach (var leftover in new Component[] { root.GetComponent<SpriteRenderer>(), root.GetComponent<BoxCollider2D>(), root.GetComponent<PlatformEffector2D>() })
            if (leftover) Undo.DestroyObjectImmediate(leftover);
        root.transform.localScale = Vector3.one;
        root.transform.rotation = Quaternion.identity;

        var scarfRenderer = Layer(root, "Scarf", scarf);
        var bodyRenderer = Layer(root, "Body", body);

        // ---- in front of / behind Kris ----
        var kris = Object.FindFirstObjectByType<PlayerController>();
        var krisRenderer = kris != null ? kris.GetComponent<SpriteRenderer>() : null;
        if (krisRenderer)
        {
            foreach (var r in new[] { scarfRenderer, bodyRenderer }) r.sortingLayerID = krisRenderer.sortingLayerID;
            scarfRenderer.sortingOrder = krisRenderer.sortingOrder - 1;
            bodyRenderer.sortingOrder = krisRenderer.sortingOrder + 1;
        }
        else Debug.LogWarning("No PlayerController (Kris) in the scene, so the body/scarf draw order was not set.");

        // ---- position: hang the knot on the branch above ----
        Vector3 p = root.transform.position;
        float halfWidth = scarfRenderer.bounds.extents.x;
        if (!FindHook(trunks, p, halfWidth, out Rect hook, out Tilemap tree))
        {
            EditorUtility.DisplayDialog("Hang Ralsei Platform", "No tree tiles found near the platform.", "OK");
            return;
        }
        if (hook.center.x < p.x - halfWidth || hook.center.x > p.x + halfWidth)
            p.x = hook.center.x;                                    // not under any branch: move it next to the nearest one
        p.y += (hook.center.y + KnotCenterBelowTop) - scarfRenderer.bounds.max.y;
        root.transform.position = p;
        root.transform.SetParent(tree.transform, true);             // fixed to the tree

        // ---- platform along his back, on the Ground layer so Kris counts as standing on it ----
        var box = bodyRenderer.GetComponent<BoxCollider2D>();
        if (!box) box = Undo.AddComponent<BoxCollider2D>(bodyRenderer.gameObject);
        FitPlatform(box, body);
        box.usedByEffector = true;
        var effector = bodyRenderer.GetComponent<PlatformEffector2D>();
        if (!effector) effector = Undo.AddComponent<PlatformEffector2D>(bodyRenderer.gameObject);
        effector.useOneWay = true;
        effector.surfaceArc = 160f;

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) bodyRenderer.gameObject.layer = ground;
        else Debug.LogWarning("There is no layer called 'Ground', so Kris will not count as standing on the platform.");

        // ---- Ralsei's HUD slot shows / hides it ----
        var switcher = Object.FindFirstObjectByType<CharacterSwitcher>();
        if (switcher != null)
        {
            var so = new SerializedObject(switcher);
            var prop = so.FindProperty("characters");
            if (prop.arraySize <= RalseiSlot) prop.arraySize = RalseiSlot + 1;
            prop.GetArrayElementAtIndex(RalseiSlot).objectReferenceValue = root;
            so.ApplyModifiedProperties();
        }
        else Debug.LogWarning("No CharacterSwitcher in the scene, so Ralsei's slot was not connected.");

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log($"'{root.name}' hangs from '{tree.name}' at {root.transform.position}. Save the scene (Ctrl+S).");
    }

    // Tools > Fix Ralsei Hang Position
    // For the tree planted into the level's own tilemaps (Tools > Plant Tree In Level Tilemaps): hangs the existing
    // Ralsei_Hook_Platform from the tip of the long branch (his body then hangs beside the tall step), makes it a child of
    // that tilemap instead of the Obs_tree prefab instance (which is not drawn in Play), and switches that duplicate tree off.
    [MenuItem("Tools/Fix Ralsei Hang Position")]
    internal static void Rehang()
    {
        var root = GameObject.Find(PlatformName);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Fix Ralsei Hang Position", "No " + PlatformName + " in the open scene. Run Tools > Hang Ralsei Platform On Tree first.", "OK");
            return;
        }
        var scarfT = root.transform.Find("Scarf");
        var scarfRenderer = scarfT != null ? scarfT.GetComponent<SpriteRenderer>() : null;
        if (!scarfRenderer)
        {
            EditorUtility.DisplayDialog("Fix Ralsei Hang Position", PlatformName + " has no Scarf child. Run Tools > Hang Ralsei Platform On Tree first.", "OK");
            return;
        }

        Tilemap trunk = null;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (tm.name == "Tilemap_Trunk" && !PrefabUtility.IsPartOfPrefabInstance(tm)) trunk = tm;
        if (trunk == null || trunk.GetUsedTilesCount() < 10)
        {
            EditorUtility.DisplayDialog("Fix Ralsei Hang Position", "The tree is not in Tilemap_Trunk yet. Run Tools > Plant Tree In Level Tilemaps first.", "OK");
            return;
        }

        var hooks = FindHooks(trunk);
        if (hooks.Count == 0)
        {
            EditorUtility.DisplayDialog("Fix Ralsei Hang Position", "Could not find a tree with a branch in Tilemap_Trunk.", "OK");
            return;
        }
        Vector3 hook = hooks[0];                                  // the biggest tree: the scarf hangs from the tip of its branch

        Undo.RecordObject(root.transform, "Re-hang Ralsei platform");
        Vector3 p = root.transform.position;
        p.x = hook.x - ScarfShaftOffset(scarfRenderer.sprite);                       // the scarf shaft (not the sprite centre) under the branch
        p.y += (hook.y + KnotCenterBelowTop) - scarfRenderer.bounds.max.y;           // knot round the branch
        root.transform.position = p;
        root.transform.SetParent(trunk.transform, true);
        int hookPoints = ConnectHooks(root, trunk, hooks, scarfRenderer);

        // One tree only: the Obs_tree prefab instance drew a second copy of the same tree. It is only pictures (its one
        // component is a static body, no colliders), and Ralsei has just been moved off it, so switch it off.
        int switchedOff = 0;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(tm)) continue;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(tm);
            if (source == null || AssetDatabase.GetAssetPath(source) != "Assets/prefabs/Obs_tree.prefab") continue;
            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(tm.gameObject);
            if (instanceRoot != null && instanceRoot.activeSelf && !root.transform.IsChildOf(instanceRoot.transform))
            {
                Undo.RecordObject(instanceRoot, "Switch off duplicate tree");
                instanceRoot.SetActive(false);
                switchedOff++;
            }
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"'{PlatformName}' now hangs from the branch tip at {hook}, at {root.transform.position}. {hookPoints} hook point(s) in the level. " +
                  $"{(switchedOff > 0 ? "The duplicate Obs_tree was switched off. " : "")}Save the scene (Ctrl+S).");
    }

    // Tools > Add Hook Points To Trees
    // Puts a HookPoint at the branch tip of every tree in Tilemap_Trunk. When Ralsei is brought in he hangs from the hook point
    // nearest to Kris, so any number of trees work: add a tree to the tilemap, run this, done.
    [MenuItem("Tools/Add Hook Points To Trees")]
    internal static void AddHookPoints()
    {
        var root = GameObject.Find(PlatformName);
        var scarfT = root != null ? root.transform.Find("Scarf") : null;
        var scarfRenderer = scarfT != null ? scarfT.GetComponent<SpriteRenderer>() : null;
        Tilemap trunk = null;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (tm.name == "Tilemap_Trunk" && !PrefabUtility.IsPartOfPrefabInstance(tm)) trunk = tm;
        if (root == null || !scarfRenderer || trunk == null)
        {
            EditorUtility.DisplayDialog("Add Hook Points", "Needs " + PlatformName + " and Tilemap_Trunk in the open scene. Run Tools > Rebuild Level Characters first.", "OK");
            return;
        }
        var hooks = FindHooks(trunk);
        int count = ConnectHooks(root, trunk, hooks, scarfRenderer);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"{count} hook point(s) for {hooks.Count} tree(s). Save the scene (Ctrl+S).");
    }

    // One hook point per tree (child of the trunk tilemap, so it moves with it) and the RalseiHang component on the platform.
    static int ConnectHooks(GameObject root, Tilemap trunk, List<Vector3> hooks, SpriteRenderer scarfRenderer)
    {
        foreach (var existing in Object.FindObjectsByType<HookPoint>(FindObjectsSortMode.None))
            if (existing.transform.IsChildOf(trunk.transform)) Undo.DestroyObjectImmediate(existing.gameObject);   // re-created below
        foreach (var h in hooks)
        {
            var go = new GameObject("Hook Point");
            Undo.RegisterCreatedObjectUndo(go, "Add hook point");
            go.transform.SetParent(trunk.transform, false);
            go.transform.position = h;
            go.AddComponent<HookPoint>();
        }

        var hang = root.GetComponent<RalseiHang>();
        if (!hang) hang = Undo.AddComponent<RalseiHang>(root);
        // where the knot sits relative to the platform's own position: hook = platform position + this
        var so = new SerializedObject(hang);
        so.FindProperty("knotOffset").vector2Value = new Vector2(ScarfShaftOffset(scarfRenderer.sprite),
            scarfRenderer.bounds.max.y - KnotCenterBelowTop - root.transform.position.y);
        so.ApplyModifiedPropertiesWithoutUndo();
        return hooks.Count;
    }

    // The hook of every tree in the tilemap, biggest tree first. A trunk is a column of at least 5 tiles; the hook is the
    // centre of the tile just past the tip of its longest branch on the right (else on the left).
    static List<Vector3> FindHooks(Tilemap trunk)
    {
        var cells = new List<Vector3Int>();
        var perColumn = new Dictionary<int, int>();
        foreach (var cell in trunk.cellBounds.allPositionsWithin)
            if (trunk.HasTile(cell)) { cells.Add(cell); perColumn[cell.x] = perColumn.TryGetValue(cell.x, out int n) ? n + 1 : 1; }

        var columns = new List<int>();
        foreach (var kv in perColumn) if (kv.Value >= 5) columns.Add(kv.Key);
        columns.Sort((a, b) => perColumn[b].CompareTo(perColumn[a]));            // biggest tree first
        var trunks = new List<int>();
        foreach (var x in columns)
            if (!trunks.Exists(t => Mathf.Abs(t - x) <= 1)) trunks.Add(x);        // a two tile wide trunk counts once

        var hooks = new List<Vector3>();
        foreach (int trunkX in trunks)
        {
            foreach (int side in new[] { 1, -1 })                                // right of the trunk first
            {
                var perRow = new Dictionary<int, int>();
                foreach (var c in cells)
                    if ((c.x - trunkX) * side > 0 && (c.x - trunkX) * side <= 6) perRow[c.y] = perRow.TryGetValue(c.y, out int n) ? n + 1 : 1;
                int branchY = int.MinValue, most = 0;
                foreach (var kv in perRow) if (kv.Value > most || (kv.Value == most && kv.Key < branchY)) { most = kv.Value; branchY = kv.Key; }
                if (branchY == int.MinValue) continue;

                int tipX = trunkX;
                foreach (var c in cells)
                    if (c.y == branchY && (c.x - trunkX) * side > 0 && (c.x - trunkX) * side <= 6 && (c.x - tipX) * side > 0) tipX = c.x;
                hooks.Add(trunk.GetCellCenterWorld(new Vector3Int(tipX + side, branchY, 0)));
                break;
            }
        }
        return hooks;
    }

    // Horizontal distance from the sprite centre to the middle of the scarf shaft, measured at the middle row of the image.
    static float ScarfShaftOffset(Sprite sprite)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(ScarfPath));
        var px = tex.GetPixels32();
        int w = tex.width, row = tex.height / 2, min = w, max = 0;
        Object.DestroyImmediate(tex);
        for (int x = 0; x < w; x++)
            if (px[row * w + x].a > 0) { min = Mathf.Min(min, x); max = Mathf.Max(max, x); }
        if (min > max) return 0f;
        return ((min + max + 1) / 2f - w / 2f) / sprite.pixelsPerUnit;
    }

    static SpriteRenderer Layer(GameObject root, string name, Sprite sprite)
    {
        var t = root.transform.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(root.transform, false);
            t = go.transform;
        }
        t.localPosition = Vector3.zero;
        var sr = t.GetComponent<SpriteRenderer>();             // (in the editor a missing component is not null, hence the bool test)
        if (!sr) sr = Undo.AddComponent<SpriteRenderer>(t.gameObject);
        sr.sprite = sprite;
        return sr;
    }

    // Both layers share one canvas size and a centre pivot, so at the same position they line up exactly.
    static Sprite ImportSprite(string path)
    {
        if (!File.Exists(path)) return null;
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return null;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.spritePixelsPerUnit = HookPpu;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        imp.SetTextureSettings(settings);
        imp.spritePivot = new Vector2(0.5f, 0.5f);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // The tree tile the knot hangs on: the lowest tile above the platform within its width, else the nearest tile.
    static bool FindHook(List<Tilemap> trunks, Vector3 p, float halfWidth, out Rect hook, out Tilemap tree)
    {
        hook = default;
        tree = null;
        float bestAbove = float.MaxValue, bestAny = float.MaxValue;
        Rect anyRect = default;
        Tilemap anyTree = null;
        foreach (var tm in trunks)
        {
            foreach (var cell in tm.cellBounds.allPositionsWithin)
            {
                if (!tm.HasTile(cell)) continue;
                var s = tm.GetSprite(cell);
                Vector3 c = tm.GetCellCenterWorld(cell);
                Vector3 scale = tm.transform.lossyScale;
                Rect r = s != null
                    ? Rect.MinMaxRect(c.x + s.bounds.min.x * scale.x, c.y + s.bounds.min.y * scale.y, c.x + s.bounds.max.x * scale.x, c.y + s.bounds.max.y * scale.y)
                    : new Rect(c.x - 0.5f, c.y - 0.5f, 1f, 1f);

                bool overlapsX = r.xMax > p.x - halfWidth && r.xMin < p.x + halfWidth;
                if (overlapsX && r.center.y > p.y && r.center.y - p.y < bestAbove)
                {
                    bestAbove = r.center.y - p.y;
                    hook = r;
                    tree = tm;
                }
                float d = ((Vector2)r.center - (Vector2)p).sqrMagnitude;
                if (d < bestAny)
                {
                    bestAny = d;
                    anyRect = r;
                    anyTree = tm;
                }
            }
        }
        if (tree != null) return true;
        hook = anyRect;
        tree = anyTree;
        return tree != null;
    }

    // Collider = a thin slab along the top of Ralsei's body (below the scarf shaft), measured from the body layer's pixels.
    static void FitPlatform(BoxCollider2D box, Sprite sprite)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(BodyPath));
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        Object.DestroyImmediate(tex);

        // the first row (from the top) where he is wide enough to stand on; his ears and the knot above are too narrow
        int bodyTop = -1;
        for (int r = 0; r < h && bodyTop < 0; r++)
        {
            int n = 0;
            for (int x = 0; x < w; x++) if (px[(h - 1 - r) * w + x].a > 0) n++;
            if (n >= 40) bodyTop = r;
        }
        if (bodyTop < 0) return;

        int probe = Mathf.Min(h - 1, bodyTop + 4), xMin = w, xMax = 0;
        for (int x = 0; x < w; x++)
            if (px[(h - 1 - probe) * w + x].a > 0) { xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x); }

        float ppu = sprite.pixelsPerUnit;
        Vector2 min = sprite.bounds.min;
        float left = min.x + xMin / ppu, right = min.x + (xMax + 1) / ppu;
        float topY = min.y + (h - bodyTop) / ppu;

        box.size = new Vector2((right - left) * 0.95f, PlatformThickness);
        box.offset = new Vector2((left + right) / 2f, topY - PlatformThickness / 2f);
    }
}
