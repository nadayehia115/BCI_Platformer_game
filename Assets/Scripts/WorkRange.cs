using UnityEngine;

// The characters only work on what the player can see: Susie on a box, Noelle on a pool, Ralsei on a tree that is on screen.
// Without this, bringing a character in at the start of the game would push a box into its hole, or freeze a pool, far
// away in the level where nobody sees it happen.
public static class WorkRange
{
    const float FallbackHalfWidth = 14f;      // used when there is no orthographic main camera
    const float Margin = 1f;                  // a little past the edge of the screen still counts

    static float CameraX
    {
        get
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position.x;
            var kris = Object.FindFirstObjectByType<PlayerController>();
            return kris != null ? kris.transform.position.x : 0f;
        }
    }

    static float HalfWidth
    {
        get
        {
            var cam = Camera.main;
            return cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect + Margin : FallbackHalfWidth;
        }
    }

    // Is this x inside what the camera shows?
    public static bool OnScreen(float x) => Mathf.Abs(x - CameraX) <= HalfWidth;

    // Does the stretch from left to right overlap what the camera shows?
    public static bool OverlapsScreen(float left, float right) => right >= CameraX - HalfWidth && left <= CameraX + HalfWidth;
}
