using UnityEngine;

// Susie's job in the level: when she is brought in with Z she appears on the left of the pushable box, walks right and
// shoves it along the ground (she is tagged StrongPlayer, which is what unfreezes the box's X movement, see PushableObject)
// until it drops into the hole. Once it has settled in the hole she is done and disappears.
[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class SusiePusher : MonoBehaviour
{
    [SerializeField] CharacterSwitcher switcher;
    [SerializeField] Rigidbody2D box;
    [Tooltip("World X of the left and right edge of the hole.")]
    [SerializeField] float holeLeft;
    [SerializeField] float holeRight;
    [SerializeField] float walkSpeed = 2.2f;
    [Tooltip("Gap between her hands and the box when she appears (world units). Slightly negative = her hands sit a touch into the box, so both hands touch it.")]
    [SerializeField] float handGap = -0.05f;
    [SerializeField] float settleSeconds = 0.5f;
    [Tooltip("Nudge if her feet float above or sink below the ground (world units).")]
    [SerializeField] float footAdjust = 0f;
    [Tooltip("Her pose while she is shoving the box (drawn facing left, so it is flipped to face the box).")]
    [SerializeField] Sprite pushSprite;

    Rigidbody2D body;
    SpriteRenderer sprite;
    Animator animator;
    Collider2D boxCollider;
    Collider2D ownCollider;
    float feetBelowPivot;            // local feet height of the standing sprite times scale (a negative number)
    float settled;
    bool done;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        ownCollider = GetComponent<Collider2D>();
        if (box != null) boxCollider = box.GetComponent<Collider2D>();

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
        if (box == null || boxCollider == null) return;
        settled = 0f;
        done = InHole();                       // the job was already done: show nothing, and Update puts her away
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
        if (settled >= settleSeconds) done = true;
    }

    bool InHole()
    {
        var b = boxCollider.bounds;
        return b.min.x >= holeLeft - 0.1f && b.max.x <= holeRight + 0.1f;
    }

    // Surface height under x: a ray straight down that ignores triggers, Susie and the box. `fallback` if nothing is hit.
    float GroundAt(float x, float fromY, float fallback)
    {
        foreach (var hit in Physics2D.RaycastAll(new Vector2(x, fromY), Vector2.down, 8f))
        {
            var c = hit.collider;
            if (c.isTrigger || c == ownCollider || c == boxCollider) continue;
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
