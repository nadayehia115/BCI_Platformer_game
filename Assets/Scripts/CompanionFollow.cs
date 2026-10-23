using UnityEngine;

// A BCI-controlled teammate (Noelle / Ralsei / Susie). It trails the keyboard player (Kris), faces the same way,
// and plays its own idle / run / jump / fall animation based on what Kris is doing. It has no physics of its own,
// so it never gets in the way or blocks the level.
[RequireComponent(typeof(SpriteRenderer))]
public class CompanionFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [Tooltip("How far behind the leader this teammate stands (world units).")]
    [SerializeField] float distanceBehind = 1.4f;
    [SerializeField] float heightOffset = 0f;
    [Tooltip("Smoothing speed; higher = sticks closer to the leader.")]
    [SerializeField] float followSpeed = 7f;
    [Tooltip("Nudge if the feet float above or sink below the leader's feet (world units).")]
    [SerializeField] float footAdjust = 0f;
    [SerializeField] float runThreshold = 0.1f;
    [SerializeField] float groundedThreshold = 0.05f;

    SpriteRenderer sprite;
    Animator animator;
    SpriteRenderer leaderSprite;
    Rigidbody2D leaderBody;
    float feetLocal;           // lowest pixel of this teammate's standing sprite, relative to its pivot
    Collider2D leaderCollider;
    float groundOffset;        // how far the real ground is above the leader's collider bottom (about 0)
    bool measured;
    float ownSpeedX;
    const float ownRunSpeed = 0.4f;     // world units per second

    void Awake()
    {
        sprite = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        feetLocal = LocalFeet(sprite);          // the scene sprite is the standing pose; the animator has not run yet
        CacheLeader();
    }

    void CacheLeader()
    {
        if (target == null) return;
        leaderSprite = target.GetComponentInChildren<SpriteRenderer>();
        leaderBody = target.GetComponent<Rigidbody2D>();
        leaderCollider = target.GetComponent<Collider2D>();
    }

    // When the BCI switches to this teammate it appears right next to the leader, not where it was left.
    void OnEnable()
    {
        if (target == null) return;
        if (leaderSprite == null) CacheLeader();
        transform.position = Goal();
        UpdateVisuals();
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (leaderSprite == null) CacheLeader();

        float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        float oldX = transform.position.x;
        transform.position = Vector3.Lerp(transform.position, Goal(), t);
        ownSpeedX = Time.deltaTime > 0f ? Mathf.Abs(transform.position.x - oldX) / Time.deltaTime : 0f;
        UpdateVisuals();
    }

    Vector3 Goal()
    {
        bool leaderFacesLeft = leaderSprite != null && leaderSprite.flipX;
        float side = leaderFacesLeft ? 1f : -1f;                     // stand behind the leader
        Vector3 goal = target.position + new Vector3(side * distanceBehind, heightOffset, 0f);

        if (leaderSprite != null && sprite != null)                  // line the feet up with the leader's feet
        {
            // Stand on the same ground as the leader: the bottom of his collider is where he touches it. (His sprite has
            // empty rows under the feet, so copying his drawn feet left this teammate floating above the ground.)
            // This teammate's own feet come from its standing pose measured once, not from the current frame: run frames
            // lift the feet a few pixels, and re-fitting every frame made the body jitter up and down.
            goal.y = GroundY() + heightOffset + footAdjust - feetLocal * transform.lossyScale.y;
        }
        return goal;
    }

    // Height of the surface the leader stands on. Whenever the leader is on the ground, a ray straight down from him is
    // measured against his collider's bottom; the difference is remembered, so the height stays right while he jumps.
    float GroundY()
    {
        if (leaderCollider == null) return leaderSprite.bounds.min.y;
        float bottom = leaderCollider.bounds.min.y;
        bool leaderGrounded = leaderBody == null || Mathf.Abs(leaderBody.linearVelocity.y) < groundedThreshold;
        if (leaderGrounded && GroundBelow(target.position.x, bottom + 0.25f, 0.85f, out float hitY))
        {
            float offset = hitY - bottom;
            if (!measured || Mathf.Abs(offset - groundOffset) > 0.02f)
                Debug.Log($"[CompanionFollow] {name}: leader collider bottom {bottom:0.000}, ground {hitY:0.000}, offset {offset:0.000}, own feet {feetLocal * transform.lossyScale.y:0.000} below pivot");
            groundOffset = offset;
            measured = true;
        }
        return bottom + groundOffset;
    }

    bool GroundBelow(float x, float fromY, float distance, out float hitY)
    {
        foreach (var hit in Physics2D.RaycastAll(new Vector2(x, fromY), Vector2.down, distance))
        {
            var c = hit.collider;
            if (c.isTrigger || c == leaderCollider || c.transform.IsChildOf(target) || c.transform.IsChildOf(transform)) continue;
            hitY = hit.point.y;
            return true;
        }
        hitY = 0f;
        return false;
    }

    // Local height of the lowest visible pixel of the sprite on `r` right now. Sprite.bounds includes the transparent
    // padding under the feet (a different amount per sprite), which made teammates float; the sprites use tight
    // meshes, so their vertices hug the art.
    static float LocalFeet(SpriteRenderer r)
    {
        var s = r.sprite;
        if (s == null) return 0f;
        float lowest = float.MaxValue;
        foreach (var v in s.vertices) lowest = Mathf.Min(lowest, v.y);
        return lowest;
    }

    void UpdateVisuals()
    {
        if (leaderSprite != null)
        {
            sprite.flipX = leaderSprite.flipX;
            sprite.sortingLayerID = leaderSprite.sortingLayerID;
            sprite.sortingOrder = leaderSprite.sortingOrder - 1;     // just behind the leader
        }

        if (animator == null || animator.runtimeAnimatorController == null) return;
        Vector2 v = leaderBody != null ? leaderBody.linearVelocity : Vector2.zero;
        // steps only while this teammate itself is moving, so the feet never run on the spot or slide when it stops
        animator.SetBool("IsRunning", ownSpeedX > ownRunSpeed);
        animator.SetBool("IsGrounded", Mathf.Abs(v.y) < groundedThreshold);
        animator.SetFloat("YVelocity", v.y);
    }
}
