using UnityEngine;

// Ralsei's hanging frame (the R-Action platform). Each time he is brought in it hangs from the HookPoint nearest to Kris
// that is on screen, so any number of trees work, nothing is assigned per tree. If there is no tree in sight he does not
// appear (like Susie with no box and Noelle with no pool). knotOffset says where the scarf knot is relative to this
// object; it is filled in by the editor tool (Tools > Fix Ralsei Hang Position / Add Hook Points To Trees).
public class RalseiHang : MonoBehaviour
{
    [SerializeField] Vector2 knotOffset;

    bool nothingToHookOn;

    void OnEnable()
    {
        nothingToHookOn = false;
        var points = FindObjectsByType<HookPoint>(FindObjectsSortMode.None);
        if (points.Length == 0) return;                          // no hook points at all: stay where he was put

        var kris = FindFirstObjectByType<PlayerController>();
        Vector2 from = kris != null ? (Vector2)kris.transform.position : (Vector2)transform.position;

        HookPoint nearest = null;
        float best = float.MaxValue;
        foreach (var point in points)
        {
            if (!WorkRange.OnScreen(point.transform.position.x)) continue;     // only a tree the player can see
            float distance = ((Vector2)point.transform.position - from).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = point;
            }
        }
        if (nearest == null)
        {
            nothingToHookOn = true;                              // put away again in Update (not inside OnEnable)
            return;
        }

        Vector2 target = (Vector2)nearest.transform.position - knotOffset;
        transform.position = new Vector3(target.x, target.y, transform.position.z);
    }

    // Is there a tree for him? x = where its branch tip is. With no hook points at all he stays where he was put (the one tree
    // he hangs from), so that spot is the tree. See PowerTargets.
    public bool FindWork(float krisX, bool onScreenOnly, float lookAhead, out float x)
    {
        x = 0f;
        var points = FindObjectsByType<HookPoint>(FindObjectsSortMode.None);
        float best = float.MaxValue;
        if (points.Length == 0)
        {
            float px = transform.position.x;
            if (!(onScreenOnly ? WorkRange.OnScreen(px) : PowerTargets.InZone(px, px, krisX, lookAhead))) return false;
            x = px;
            return true;
        }
        foreach (var point in points)
        {
            float px = point.transform.position.x;
            bool wanted = onScreenOnly ? WorkRange.OnScreen(px) : PowerTargets.InZone(px, px, krisX, lookAhead);
            if (!wanted || Mathf.Abs(px - krisX) >= best) continue;
            best = Mathf.Abs(px - krisX);
            x = px;
        }
        return best < float.MaxValue;
    }

    void Update()
    {
        if (!nothingToHookOn) return;
        nothingToHookOn = false;
        var switcher = FindFirstObjectByType<CharacterSwitcher>();
        if (switcher != null && switcher.IsShown) switcher.Confirm();        // the same as pressing Z: nobody is on screen
        else gameObject.SetActive(false);
    }
}
