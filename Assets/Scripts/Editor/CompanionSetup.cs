using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Add Companions To Scene
// Adds Ralsei, Noelle and Susie to the open scene as followers of the keyboard player (Kris) and connects them to the
// HUD's CharacterSwitcher, so only the character the BCI selected is shown. The order matches the HUD portraits.
// Run Tools > Build Character Animations first (it creates the animator controllers this uses).
public static class CompanionSetup
{
    // HUD slot order. Ralsei's slot is not a follower: he is the platform hanging on the tree
    // (Tools > Hang Ralsei Platform On Tree), so this tool leaves slot 0 as it is. Susie is created here as a follower;
    // run Tools > Setup Susie Box Push afterwards to make her the one who pushes the box.
    static readonly string[] Names = { null, "Noelle", "Susie" };

    // All sprites are imported at 16 pixels per unit, like Kris. Their art has more pixels than Kris's (36 px tall),
    // so each one is scaled down to stand about as tall as him: 36 / body height in pixels (Ralsei 50, Noelle 115, Susie 51).
    // Tweak the Transform scale on a character if you want it a bit bigger or smaller.
    static readonly float[] Scales = { 0.72f, 0.31f, 0.70f };

    [MenuItem("Tools/Add Companions To Scene")]
    static void Add()
    {
        var kris = Object.FindFirstObjectByType<PlayerController>();
        if (kris == null)
        {
            EditorUtility.DisplayDialog("Add Companions", "No PlayerController (Kris) found in the open scene. Open GameScene first.", "OK");
            return;
        }
        var switcher = Object.FindFirstObjectByType<CharacterSwitcher>();

        // re-running replaces the previous set
        var old = GameObject.Find("Companions");
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject("Companions");
        Undo.RegisterCreatedObjectUndo(root, "Add companions");

        var created = new List<GameObject>();
        for (int i = 0; i < Names.Length; i++)
        {
            string n = Names[i];
            if (n == null)                                   // keep whatever is already in this slot (the hanging Ralsei)
            {
                var kept = switcher != null ? new SerializedObject(switcher).FindProperty("characters") : null;
                created.Add(kept != null && kept.arraySize > i ? kept.GetArrayElementAtIndex(i).objectReferenceValue as GameObject : null);
                continue;
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Characters/{n}/{n}_idle_0.png");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"Assets/Characters/{n}/{n}_Anim.controller");
            if (sprite == null || controller == null)
            {
                Debug.LogError($"Missing art for {n}. Run Tools > Build Character Animations first.");
                continue;
            }

            var go = new GameObject(n);
            go.transform.SetParent(root.transform, false);
            go.transform.position = kris.transform.position + Vector3.left * 1.4f;
            go.transform.localScale = Vector3.one * Scales[i];

            go.AddComponent<SpriteRenderer>().sprite = sprite;
            go.AddComponent<Animator>().runtimeAnimatorController = controller;

            var follow = go.AddComponent<CompanionFollow>();
            var so = new SerializedObject(follow);
            so.FindProperty("target").objectReferenceValue = kris.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(i == 0);        // CharacterSwitcher shows the selected one when the game starts
            created.Add(go);
        }

        if (switcher != null && created.Count == Names.Length)
        {
            var sw = new SerializedObject(switcher);
            var prop = sw.FindProperty("characters");
            prop.arraySize = created.Count;
            for (int i = 0; i < created.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = created[i];
            sw.ApplyModifiedProperties();
        }
        else if (switcher == null)
        {
            Debug.LogWarning("No CharacterSwitcher in the scene, so the companions are not connected to the HUD.");
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"Added {created.Count} companions under 'Companions'. Save the scene (Ctrl+S).");
    }
}
