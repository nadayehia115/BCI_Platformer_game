using UnityEditor;
using UnityEngine;

// Tools > Rebuild Level Characters
// The characters, the tree copy and the box pusher live only in the scene file (their scripts are separate), so a merge
// that brings in another version of GameScene wipes them. This runs the whole setup again in the right order, on the open scene:
//   1. Add Companions To Scene      - Noelle and Susie as followers, connected to the HUD switcher
//   2. Hang Ralsei Platform On Tree - Ralsei's frame, in the HUD's first slot
//   3. Setup Susie Box Push         - Susie pushes the box into the hole
//   4. Setup Noelle Ice             - Noelle freezes the water into ice
//   5. Plant Tree In Level Tilemaps - the tree drawn by the level's own tilemaps (it shows in the Game view)
//   6. Fix Ralsei Hang Position     - Ralsei on the tip of that tree's branch (plus a hook point for every tree);
//                                     the duplicate prefab tree is switched off
// Before running it, put the tree where Ralsei should hang in the middle of the Scene view (he is hung on the nearest tree).
public static class LevelCharactersSetup
{
    [MenuItem("Tools/Rebuild Level Characters")]
    static void Rebuild()
    {
        CompanionSetup.Add();
        HookPlatformSetup.Hang();
        SusiePushSetup.Setup();
        NoelleIceSetup.Setup();
        PlantTreeSetup.Plant();
        HookPlatformSetup.Rehang();
        Debug.Log("Level characters rebuilt. Save the scene (Ctrl+S).");
    }
}
