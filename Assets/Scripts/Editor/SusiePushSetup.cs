using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Tilemaps;

// Tools > Setup Susie Box Push
// Turns Susie (under Companions) from a follower into the one who pushes the pushable box into the hole:
//   * removes her CompanionFollow, gives her a kinematic body + a collider round her body (not the axe) and the
//     StrongPlayer tag, which is what unfreezes the box's X movement (PushableObject)
//   * she finds the box (any number of them) and its hole by herself while the game runs, nothing is assigned per box
// Run it after Tools > Add Companions To Scene (re-running that tool brings the follower Susie back).
public static class SusiePushSetup
{
    const string StrongTag = "StrongPlayer";

    [MenuItem("Tools/Setup Susie Box Push")]
    internal static void Setup()
    {
        CompanionFollow susieFollow = null;
        foreach (var f in Object.FindObjectsByType<CompanionFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (f.name == "Susie") susieFollow = f;
        GameObject susie = susieFollow != null ? susieFollow.gameObject : null;
        if (susie == null)
        {
            foreach (var p in Object.FindObjectsByType<SusiePusher>(FindObjectsInactive.Include, FindObjectsSortMode.None)) susie = p.gameObject;
        }
        if (susie == null)
        {
            EditorUtility.DisplayDialog("Setup Susie Box Push", "No Susie found. Run Tools > Add Companions To Scene first.", "OK");
            return;
        }

        int boxes = Object.FindObjectsByType<PushableObject>(FindObjectsSortMode.None).Length;
        if (boxes == 0) Debug.LogWarning("There is no pushable box (PushableObject) in the open scene yet; Susie will have nothing to push.");

        if (susieFollow != null) Undo.DestroyObjectImmediate(susieFollow);

        // ---- body, collider and tag ----
        var rb = susie.GetComponent<Rigidbody2D>();
        if (!rb) rb = Undo.AddComponent<Rigidbody2D>(susie);
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.freezeRotation = true;

        var sr = susie.GetComponent<SpriteRenderer>();
        var pose = ImportPushSprite();
        var col = susie.GetComponent<BoxCollider2D>();
        if (!col) col = Undo.AddComponent<BoxCollider2D>(susie);
        FitBodyCollider(col, pose != null ? pose : sr.sprite, pose != null);

        EnsureTag(StrongTag);
        susie.tag = StrongTag;

        // ---- the pusher ----
        var pusher = susie.GetComponent<SusiePusher>();
        if (!pusher) pusher = Undo.AddComponent<SusiePusher>(susie);
        var so = new SerializedObject(pusher);
        so.FindProperty("switcher").objectReferenceValue = Object.FindFirstObjectByType<CharacterSwitcher>();
        so.FindProperty("pushSprite").objectReferenceValue = pose;
        so.ApplyModifiedPropertiesWithoutUndo();

        // same sorting layer as Kris so she is drawn with him, not behind the ground
        var kris = Object.FindFirstObjectByType<PlayerController>();
        var krisRenderer = kris != null ? kris.GetComponent<SpriteRenderer>() : null;
        if (krisRenderer)
        {
            sr.sortingLayerID = krisRenderer.sortingLayerID;
            sr.sortingOrder = krisRenderer.sortingOrder;
        }

        EditorSceneManager.MarkSceneDirty(susie.scene);
        Selection.activeGameObject = susie;
        Debug.Log($"Susie is ready: she pushes the nearest of the {boxes} box(es) in the scene into the hole to its right, one box per Z. Save the scene (Ctrl+S).");
    }

    // A box around the whole drawn pose, hands to feet, so the front of the collider is where her hands are and she touches
    // the box exactly. `mirrored`: the sprite is shown flipped (the pushing pose faces left), and colliders do not flip.
    static void FitBodyCollider(BoxCollider2D col, Sprite sprite, bool mirrored)
    {
        if (sprite == null) return;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in sprite.vertices)
        {
            float x = mirrored ? -v.x : v.x;
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
        }
        col.size = new Vector2(maxX - minX, maxY - minY);
        col.offset = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
    }

    // Her pushing pose, imported like her other frames: 16 pixels per unit, point filtering, same canvas and centre pivot.
    static Sprite ImportPushSprite()
    {
        const string path = "Assets/Characters/Susie/Susie_push.png";
        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Debug.LogWarning("Missing " + path + ", Susie will keep her run animation while pushing."); return null; }
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.spritePixelsPerUnit = 16;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        imp.SetTextureSettings(settings);
        imp.spritePivot = new Vector2(0.5f, 0.5f);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void EnsureTag(string tag)
    {
        foreach (var t in InternalEditorUtility.tags)
            if (t == tag) return;
        InternalEditorUtility.AddTag(tag);
    }
}
