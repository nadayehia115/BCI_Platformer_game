using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float jumpForce = 6f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckDistance = 0.1f;
    [Tooltip("How long the Animator still counts Kris as grounded after losing the ground (stops flicker)")]
    [SerializeField] private float groundedGraceTime = 0.1f;

    [Header("Components")]
    [SerializeField] private Rigidbody2D rigidBody;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator _animator;
    [SerializeField] private Collider2D col;

    private float horizontalInput;
    private float lastGroundedTime;

    private void Start()
    {
        // Cache references automatically if left unassigned in Inspector
        if (rigidBody == null) rigidBody = GetComponent<Rigidbody2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (col == null) col = GetComponent<Collider2D>();
        if (_animator == null) _animator = GetComponent<Animator>();

        // Warn if the Animator has no Bool parameter named exactly "IsRunning" (case-sensitive)
        if (_animator != null)
        {
            bool found = false;
            foreach (AnimatorControllerParameter p in _animator.parameters)
            {
                if (p.name == "IsRunning" && p.type == AnimatorControllerParameterType.Bool)
                    found = true;
            }
            if (!found)
                Debug.LogWarning("PlayerController: Animator has no Bool parameter named exactly 'IsRunning'.");
        }
        else
        {
            Debug.LogWarning("PlayerController: no Animator found, so animations won't change.");
        }

        // If the Inspector field was left empty (defaults to "Nothing"), use the "Ground" layer by name
        if (groundLayer.value == 0)
        {
            groundLayer = LayerMask.GetMask("Ground");
            if (groundLayer.value == 0)
                Debug.LogWarning("PlayerController: no Ground layer set and no layer named 'Ground' exists, so the player can never be grounded.");
        }
    }

    private void Update()
    {
        // Read raw input in Update
        horizontalInput = Input.GetAxisRaw("Horizontal");

        // Handle Visual Flip
        FlipCharacter();

        // Check the ground once per frame and reuse the result
        bool isGrounded = GetIsGrounded();
        if (isGrounded) lastGroundedTime = Time.time;

        // Handle Jump (only when standing on the ground)
        if (Input.GetKeyDown(KeyCode.W) && isGrounded)
        {
            Jump();
            lastGroundedTime = -100f; // leave the ground immediately for the animation
        }

        // Handle Animator
        if (_animator != null)
        {
            // Kris counts as grounded for a split second after losing the ground,
            // so a one-frame flicker while walking doesn't start the fall animation
            bool animGrounded = Time.time - lastGroundedTime <= groundedGraceTime;

            _animator.SetBool("IsRunning", horizontalInput != 0);
            _animator.SetBool("IsGrounded", animGrounded);
            _animator.SetFloat("YVelocity", rigidBody.linearVelocity.y);
        }
    }

    private void FixedUpdate()
    {
        // Apply physics velocity strictly inside FixedUpdate
        rigidBody.linearVelocity = new Vector2(horizontalInput * speed, rigidBody.linearVelocity.y);
    }

    private void FlipCharacter()
    {
        if (spriteRenderer == null) return;

        if (horizontalInput > 0)
            spriteRenderer.flipX = false;
        else if (horizontalInput < 0)
            spriteRenderer.flipX = true;
    }

    private bool GetIsGrounded()
    {
        // Not grounded while moving upward (blocks a second jump right after takeoff)
        if (rigidBody.linearVelocity.y > 0.1f) return false;

        // Cast the player's collider shape straight down, just below the feet
        Bounds b = col.bounds;
        RaycastHit2D[] hits = Physics2D.BoxCastAll(
            b.center,
            new Vector2(b.size.x * 0.9f, b.size.y),
            0f,
            Vector2.down,
            groundCheckDistance,
            groundLayer
        );

        // Ignore the player's own collider
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider != null && hit.collider != col)
                return true;
        }
        return false;
    }

    private void Jump()
    {
        rigidBody.linearVelocity = new Vector2(rigidBody.linearVelocity.x, 0);
        rigidBody.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }
}