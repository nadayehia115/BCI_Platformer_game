using System.Collections.Generic;
using UnityEngine;

// Susie's job in the level: when she is brought in she picks the pushable box nearest to Kris that has not been put away yet,
// appears against its left side, walks right and shoves it along the ground (she is tagged StrongPlayer, which is what
// unfreezes a box's X movement, see PushableObject) until it drops into the hole to its right. Once it has settled in the
// hole she is done and disappears; the next time she is brought in she takes the next box.
// Any number of boxes (PushableObject) in the scene work: nothing has to be assigned per box, the hole is found by looking
// for the first gap in the ground to the right of the box.
[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class SusiePusher : MonoBehaviour
{
    [SerializeField] CharacterSwitcher switcher;
    [SerializeField] float walkSpeed = 2.2f;
    [Tooltip("Gap between her hands and the box when she appears (world units). Slightly negative = her hands sit a touch into the box, so both hands touch it.")]
    [SerializeField] float handGap = -0.05f;
    [SerializeField] float settleSeconds = 0.5f;
    [Tooltip("Nudge if her feet float above or sink below the ground (world units).")]
    [SerializeField] float footAdjust = 0f;
    [Tooltip("Her pose while she is shoving the box (drawn facing left, so it is flipped to face the box).")]
    [SerializeField] Sprite pushSprite;
    [Tooltip("How far to the right of a box she looks for a hole (world units).")]
    [SerializeField] float holeSearchDistance = 60f;

    Rigidbody2D body;
    SpriteRenderer sprite;
    Animator animator;
    Collider2D ownCollider;
    float feetBelowPivot;            // local feet height of the standing sprite times scale (a negative number)

    // the box she is working on, and the hole it has to go into
    Rigidbody2D box;
    Collider2D boxCollider;
    float holeLeft, holeRight;
    readonly HashSet<Rigidbody2D> finished = new HashSet<Rigidbody2D>();    // boxes already in their holes
    float settled;
    bool done;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        ownCollider = GetComponent<Collider2D>();

        ShowPose();
        FitColliderToPose();
        float lowest = 0f;
        if (sprite.sprite != null)
        {
            lowest = float.MaxValue;
            foreach (var v in sprite.sprite.vertices) lowest = Mathf.Min(lowest, v.y);
        }
        feetBelowPivot = lowest * transform.lossyScale.y;
    }

    void OnEnable()
    {
        settled = 0f;
        done = !ChooseBox();                   // no box left to push: show nothing, and Update puts her away
        sprite.enabled = !done;
        if (ownCollider != null) ownCollider.enabled = !done;
        if (done) return;

        ShowPose();
        // her front edge (the extended hands) starts right against the box, no gap
        float reach = FrontReach();
        float x = boxCollider.bounds.min.x - reach - handGap;
        float ground = GroundAt(x, boxCollider.bounds.min.y + 1.5f, boxCollider.bounds.min.y);
        body.position = new Vector2(x, ground - feetBelowPivot + footAdjust);
        transform.position = body.position;
    }

    // ---- choosing the box and finding its hole ----

    // The box nearest to Kris (to her, if there is no Kris) that is on screen, not in a hole yet, and has a hole to its right.
    bool ChooseBox()
    {
        var kris = FindFirstObjectByType<PlayerController>();
        float from = kris != null ? kris.transform.position.x : transform.position.x;

        Rigidbody2D best = null;
        float bestDistance = float.MaxValue, bestLeft = 0f, bestRight = 0f;
        foreach (var pushable in FindObjectsByType<PushableObject>(FindObjectsSortMode.None))
        {
            var rb = pushable.GetComponent<Rigidbody2D>();
            var col = pushable.GetComponent<Collider2D>();
            if (rb == null || col == null || finished.Contains(rb)) continue;
            if (!WorkRange.OnScreen(col.bounds.center.x)) continue;        // only a box the player can see
            if (!FindHole(col, out float left, out float right)) continue;

            float distance = Mathf.Abs(col.bounds.center.x - from);
            if (distance < bestDistance)
            {
                best = rb;
                bestDistance = distance;
                bestLeft = left;
                bestRight = right;
            }
        }
        if (best == null) return false;

        box = best;
        boxCollider = best.GetComponent<Collider2D>();
        holeLeft = bestLeft;
        holeRight = bestRight;
        return true;
    }

    // The hole = the first stretch to the right of the box where there is no ground at the level the box stands on.
    bool FindHole(Collider2D boxCol, out float left, out float right)
    {
        left = right = 0f;
        const float step = 0.25f;
        var b = boxCol.bounds;
        float x = b.max.x + step, limit = b.max.x + holeSearchDistance;

        while (x < limit && HasGround(x, b.min.y)) x += step;                  // along the ground to the first gap
        if (x >= limit) return false;
        left = Refine(x - step, x, b.min.y, groundAtLow: true);

        while (x < limit && !HasGround(x, b.min.y)) x += step;                 // and across the gap
        if (x >= limit) return false;
        right = Refine(x - step, x, b.min.y, groundAtLow: false);
        return right > left;
    }

    // Bisect between a point without / with ground to pin the edge down to a few millimetres.
    float Refine(float low, float high, float level, bool groundAtLow)
    {
        for (int i = 0; i < 6; i++)
        {
            float mid = (low + high) / 2f;
            if (HasGround(mid, level) == groundAtLow) low = mid; else high = mid;
        }
        return (low + high) / 2f;
    }

    // Is there ground at the box's level at this x? (a short ray from just above the level to just below it)
    bool HasGround(float x, float level)
    {
        foreach (var hit in Physics2D.RaycastAll(new Vector2(x, level + 0.3f), Vector2.down, 0.8f))
            if (IsGround(hit.collider)) return true;
        return false;
    }

    // Anything solid that is not Susie, a pushable box, or the player counts as ground.
    bool IsGround(Collider2D c)
    {
        if (c.isTrigger || c == ownCollider || c.GetComponent<PushableObject>() != null) return false;
        var rb = c.attachedRigidbody;
        return rb == null || (rb.GetComponent<PlayerController>() == null && rb.GetComponent<PushableObject>() == null);
    }

    // ---- the pose and the collider ----

    // She is always drawn in her pushing pose (no idle / run animation): the animator is switched off and the pose,
    // drawn facing left, is flipped to face the box.
    void ShowPose()
    {
        if (animator != null) animator.enabled = false;
        if (pushSprite != null) sprite.sprite = pushSprite;
        sprite.flipX = pushSprite != null;
    }

    // Her collider is exactly the drawn pose, hands to feet (flipped like the sprite, because colliders do not flip with
    // flipX), so the front of the collider is the tips of her hands. Done here so it never depends on an editor tool.
    void FitColliderToPose()
    {
        if (!(ownCollider is BoxCollider2D bc) || sprite.sprite == null) return;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in sprite.sprite.vertices)
        {
            float x = sprite.flipX ? -v.x : v.x;
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
        }
        bc.size = new Vector2(maxX - minX, maxY - minY);
        bc.offset = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
    }

    // Distance from her pivot to the front of her collider (the side that touches the box).
    float FrontReach()
    {
        if (ownCollider is BoxCollider2D bc) return (bc.offset.x + bc.size.x / 2f) * transform.lossyScale.x;
        return ownCollider != null ? ownCollider.bounds.max.x - transform.position.x : 0f;
    }

    // ---- the push ----

    void Update()
    {
        if (done) Finish();
    }

    void FixedUpdate()
    {
        if (done || box == null || boxCollider == null) return;

        bool boxPastLip = boxCollider.bounds.min.x >= holeLeft;      // its left edge is over the hole, so it has dropped
        bool walking = !boxPastLip;

        Vector2 pos = body.position;
        if (walking) pos.x += walkSpeed * Time.fixedDeltaTime;
        float ground = GroundAt(pos.x, pos.y + 1f, pos.y + feetBelowPivot);
        pos.y = ground - feetBelowPivot + footAdjust;
        body.MovePosition(pos);

        if (InHole() && box.linearVelocity.sqrMagnitude < 0.01f) settled += Time.fixedDeltaTime; else settled = 0f;
        if (settled >= settleSeconds)
        {
            finished.Add(box);                                       // this box is put away; the next time she takes another one
            done = true;
        }
    }

    bool InHole()
    {
        var b = boxCollider.bounds;
        return b.min.x >= holeLeft - 0.1f && b.max.x <= holeRight + 0.1f;
    }

    // Surface height under x: a ray straight down that ignores triggers, Susie and the boxes. `fallback` if nothing is hit.
    float GroundAt(float x, float fromY, float fallback)
    {
        foreach (var hit in Physics2D.RaycastAll(new Vector2(x, fromY), Vector2.down, 8f))
        {
            var c = hit.collider;
            if (c.isTrigger || c == ownCollider || c.GetComponent<PushableObject>() != null) continue;
            return hit.point.y;
        }
        return fallback;
    }

    // Hide her the same way pressing Z would, so the switcher knows nobody is on screen.
    void Finish()
    {
        done = false;
        if (switcher != null && switcher.IsShown) switcher.Confirm();
        else gameObject.SetActive(false);
    }
}
