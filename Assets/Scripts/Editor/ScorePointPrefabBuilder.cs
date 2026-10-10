using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools > Build Score Point Prefab : turns the empty Assets/prefabs/score_points.prefab into the gem collectible.
// The prefab is edited in place, so its GUID stays the same and every instance already placed in a level picks it up.
public static class ScorePointPrefabBuilder
{
    const string PrefabPath = "Assets/prefabs/score_points.prefab";
    const string SheetPath = "Assets/tiles/tileArt/tilemap_packed.png";
    const string GemSprite = "tilemap_packed_67";

    [MenuItem("Tools/Build Score Point Prefab")]
    static void Build()
    {
        var gem = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().FirstOrDefault(s => s.name == GemSprite);
        if (gem == null)
        {
            Debug.LogError("Gem sprite " + GemSprite + " not found in " + SheetPath);
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // start clean so running the tool twice gives the same result
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach (var c in root.GetComponents<Collider2D>()) Object.DestroyImmediate(c);
            foreach (var s in root.GetComponents<ScorePoint>()) Object.DestroyImmediate(s);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = gem;
            sr.sortingOrder = 10;          // above the tilemaps

            var col = root.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.4f;

            var point = root.AddComponent<ScorePoint>();
            var so = new SerializedObject(point);
            so.FindProperty("visual").objectReferenceValue = visual.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("Score point prefab built: " + PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
