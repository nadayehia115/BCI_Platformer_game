using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 5f;
    [SerializeField] private float jumpForce = 6f;
    
    [Header("Components")]
    [SerializeField] private Rigidbody2D rigidBody;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator _animator;

    private Vector2 screenBounds;
    private float playerHalfWidth;
    private float playerHalfHeight;
    private float xPosLastFrame;

    private void Start()
    {
        screenBounds = Camera.main.ScreenToWorldPoint(new Vector2(Screen.width, Screen.height));
        
        // Ensure spriteRenderer is assigned to avoid errors
        if (spriteRenderer != null)
        {
            playerHalfWidth = spriteRenderer.bounds.extents.x;
            playerHalfHeight = spriteRenderer.bounds.extents.y;
        }
    }

    void Update()
    {
        HandleMovement();
        ClampMovement();
        FlipCharacterx();

        // Checks for W key and the raycast grounding logic
        if (Input.GetKeyDown(KeyCode.W) && GetIsGrounded())
        {
            Jump();
        }
    }

    private void HandleMovement()
    {
        // FIX 1: GetAxisRaw ignores artificial smoothing, fixing the ghost movement
        float input = Input.GetAxisRaw("Horizontal");
        
        // FIX 2: Assigning velocity directly to the Rigidbody allows the physics engine to prevent wall clipping
        rigidBody.linearVelocity = new Vector2(input * speed, rigidBody.linearVelocity.y);

        // Safety check so the dummy box doesn't crash without an animator
        if (_animator != null)
        {
            _animator.SetBool("IsRunning", input != 0);
        }
    }

    private void ClampMovement()
    {
        float clampedX = Mathf.Clamp(transform.position.x, -screenBounds.x + playerHalfWidth, screenBounds.x - playerHalfWidth);
        Vector2 pos = transform.position;
        pos.x = clampedX;
        transform.position = pos;
    }

    private void FlipCharacterx()
    {
        float input = Input.GetAxisRaw("Horizontal"); // Matched to GetAxisRaw
        
        if (input > 0 && (transform.position.x > xPosLastFrame))
            spriteRenderer.flipX = false;
        else if (input < 0 && (transform.position.x < xPosLastFrame))
            spriteRenderer.flipX = true;
            
        xPosLastFrame = transform.position.x;
    }

    private bool GetIsGrounded()
    {
        // Fires a raycast slightly past the bottom of the sprite to detect the "Ground" layer
        return Physics2D.Raycast(transform.position, Vector2.down, playerHalfHeight + 0.1f, LayerMask.GetMask("Ground"));
    }

    private void Jump()
    {
        // Zeros out vertical momentum before jumping to ensure consistent jump heights
        rigidBody.linearVelocity = new Vector2(rigidBody.linearVelocity.x, 0);
        rigidBody.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }
}