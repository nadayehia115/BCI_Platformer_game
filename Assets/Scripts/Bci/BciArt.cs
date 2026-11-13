using UnityEngine;

// The few sprites the BCI screens take from the game's own art (Assets/Resources/BciArt.asset, made by BciSetup): tiles of the
// game's tileset (Assets/Tiles/tileArt/tilemap_packed.png) for the ground and the crates of the loading screen's mini-game.
public class BciArt : ScriptableObject
{
    public Sprite grass;     // tilemap_packed_2: a grass-topped ground tile
    public Sprite dirt;      // tilemap_packed_5: plain earth
    public Sprite crate;     // tilemap_packed_26: the small wooden crate
}
