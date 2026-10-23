using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Tools > Plant Tree In Level Tilemaps
// The Obs_tree prefab instance is drawn in the Scene view but not in the Game view. This copies its tiles into the level's
// own tilemaps (the ones that draw the ground, grass and leaves in the Game view) at the same place, snapped to the grid, so
// the tree stands on the tall step even if the prefab instance is not drawn. The prefab instance itself is left alone, so
// whatever hangs from it (Ralsei) is not touched.
//   prefab "trunk" -> Tilemap_Trunk      prefab "lb" -> Tilemap_Leaves_b
//   prefab "lf"    -> Tilemap_Leaves_f   prefab "hang" -> Tilemap_Decorations
public static class PlantTreeSetup
{
    const string PrefabPath = "Assets/prefabs/Obs_tree.prefab";

    [MenuItem("Tools/Plant Tree In Level Tilemaps")]
    internal static void Plant()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Plant Tree", "Missing " + PrefabPath, "OK");
            return;
        }

        // where the prefab instance sits in the scene: the world position of its cell (0,0), rounded to a whole cell
        Tilemap instanceTilemap = null;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (PrefabUtility.IsPartOfPrefabInstance(tm) && PrefabUtility.GetCorrespondingObjectFromSource(tm) != null
                && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(tm)) == PrefabPath)
                instanceTilemap = tm;
        if (instanceTilemap == null)
        {
            EditorUtility.DisplayDialog("Plant Tree", "No Obs_tree instance found in the open scene. Open GameScene first.", "OK");
            return;
        }
        Vector3 origin = instanceTilemap.CellToWorld(Vector3Int.zero);
        var shift = new Vector3Int(Mathf.RoundToInt(origin.x), Mathf.RoundToInt(origin.y), 0);

        var pairs = new (string source, string target)[]
        {
            ("trunk", "Tilemap_Trunk"), ("lb", "Tilemap_Leaves_b"), ("lf", "Tilemap_Leaves_f"), ("hang", "Tilemap_Decorations"),
        };
        var sceneMaps = new Dictionary<string, Tilemap>();
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (!PrefabUtility.IsPartOfPrefabInstance(tm)) sceneMaps[tm.name] = tm;

        int copied = 0;
        foreach (var (source, target) in pairs)
        {
            var src = FindChildTilemap(prefab, source);
            if (src == null || !sceneMaps.TryGetValue(target, out var dst))
            {
                Debug.LogWarning($"Plant Tree: skipped '{source}' -> '{target}' (one of them was not found).");
                continue;
            }
            Undo.RecordObject(dst, "Plant tree");
            foreach (var cell in src.cellBounds.allPositionsWithin)
            {
                var tile = src.GetTile(cell);
                if (tile == null) continue;
                dst.SetTile(cell + shift, tile);
                copied++;
            }
            EditorUtility.SetDirty(dst);
        }

        EditorSceneManager.MarkSceneDirty(instanceTilemap.gameObject.scene);
        Debug.Log($"Planted the tree: copied {copied} tiles, shifted by {shift}. Save the scene (Ctrl+S).");
    }

    static Tilemap FindChildTilemap(GameObject prefab, string name)
    {
        foreach (var tm in prefab.GetComponentsInChildren<Tilemap>(true))
            if (tm.gameObject.name == name) return tm;
        return null;
    }
}
