using UnityEngine;

// The few sprites the BCI screens take from the game's own art (Assets/Resources/BciArt.asset, made by BciSetup): tiles of the
// game's tileset (Assets/Tiles/tileArt/tilemap_packed.png) for the ground and the crates of the loading screen's mini-game, and the
// poses of Tenna (Assets/deltarune/Tenna_*.png), the character who speaks in the setup screens (BciSpeechBubble).
public class BciArt : ScriptableObject
{
    public Sprite grass;     // tilemap_packed_2: a grass-topped ground tile
    public Sprite dirt;      // tilemap_packed_5: plain earth
    public Sprite crate;     // tilemap_packed_26: the small wooden crate

    public Sprite tennaTalk;     // Tenna_Pointup_pose: finger up, "listen!" (the normal speaking pose)
    public Sprite tennaPoint;    // Tenna_point_at_screen: points at the player / the screen
    public Sprite tennaLaugh;    // Tenna_laughing: for good news
}
