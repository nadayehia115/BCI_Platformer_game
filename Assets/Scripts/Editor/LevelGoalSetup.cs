using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Tools > Add Level Goal At Flag
// Puts a "Level Goal" object where the flag is (the right-most tile of Tilemap_Decorations), so the time limit can be set
// in the Inspector and the goal zone can be moved. Not needed for the game to work: without it the game finds the flag by
// itself and uses the default time limit of 240 seconds.
public static class LevelGoalSetup
{
    [MenuItem("Tools/Add Level Goal At Flag")]
    static void Add()
    {
        var existing = Object.FindFirstObjectByType<LevelGoal>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        Tilemap decorations = null;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (tm.name == "Tilemap_Decorations") decorations = tm;
        if (decorations == null)
        {
            EditorUtility.DisplayDialog("Add Level Goal", "No Tilemap_Decorations in the open scene. Open GameScene first.", "OK");
            return;
        }

        bool found = false;
        Vector3Int flag = default;
        foreach (var cell in decorations.cellBounds.allPositionsWithin)
            if (decorations.HasTile(cell) && (!found || cell.x > flag.x)) { flag = cell; found = true; }
        if (!found) return;

        var go = new GameObject("Level Goal");
        Undo.RegisterCreatedObjectUndo(go, "Add level goal");
        go.transform.position = decorations.GetCellCenterWorld(flag);
        go.AddComponent<LevelGoal>();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log($"Level Goal added at {go.transform.position}. Set the time limit on it in the Inspector, then save the scene (Ctrl+S).");
    }
}
