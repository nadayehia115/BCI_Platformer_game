using UnityEngine;

// Which friend's power the obstacle in front of Kris needs: a pool of water needs Noelle (she freezes it), a tree needs Ralsei
// (he hangs from its branch and Kris stands on him), a box needs Susie (she pushes it into the hole). Used by
//   * CharacterSwitcher.AutoSelect: the Python "t" (the classification says the player kept the focus) or the S key brings in
//     the friend the nearest obstacle needs, nobody has to pick one;
//   * SsvepBoard: the flashing pads are only shown while such an obstacle is near, so the stretches between them are rest.
// The helpers (NoelleFreeze, RalseiHang, SusiePusher) say whether they have something to do and where; nothing is assigned
// per obstacle, so any number of pools, trees and boxes work.
public static class PowerTargets
{
    public const int Ralsei = 0, Noelle = 1, Susie = 2;          // the order of the HUD portraits

    public struct Target
    {
        public int power;            // Ralsei / Noelle / Susie
        public float x;              // where the obstacle is
        public float distance;       // from Kris
    }

    // The nearest obstacle that needs a power. onScreenOnly: only what the camera shows (what the friends work on). Otherwise
    // everything up to lookAhead units in front of Kris (a little behind him still counts).
    public static bool Nearest(bool onScreenOnly, float lookAhead, out Target best)
    {
        best = new Target { distance = float.MaxValue };
        var kris = Object.FindFirstObjectByType<PlayerController>();
        float krisX = kris != null ? kris.transform.position.x : 0f;

        foreach (var friend in Object.FindObjectsByType<RalseiHang>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (friend.FindWork(krisX, onScreenOnly, lookAhead, out float x)) Consider(Ralsei, x, krisX, ref best);
        foreach (var friend in Object.FindObjectsByType<NoelleFreeze>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (friend.FindWork(krisX, onScreenOnly, lookAhead, out float x)) Consider(Noelle, x, krisX, ref best);
        foreach (var friend in Object.FindObjectsByType<SusiePusher>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (friend.FindWork(krisX, onScreenOnly, lookAhead, out float x)) Consider(Susie, x, krisX, ref best);

        return best.distance < float.MaxValue;
    }

    static void Consider(int power, float x, float krisX, ref Target best)
    {
        float distance = Mathf.Abs(x - krisX);
        if (distance >= best.distance) return;
        best = new Target { power = power, x = x, distance = distance };
    }

    // Is something at this stretch of the level in the zone (in front of Kris within lookAhead, or a little behind)?
    public static bool InZone(float left, float right, float krisX, float lookAhead) =>
        right >= krisX - BehindMargin && left <= krisX + lookAhead;

    public const float BehindMargin = 2f;
}
