using UnityEngine;

public class PushableObject : MonoBehaviour
{
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Only unfreeze X movement if the colliding object is tagged StrongPlayer
        if (collision.gameObject.CompareTag("StrongPlayer"))
        {
            rb.constraints &= ~RigidbodyConstraints2D.FreezePositionX;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        // Re-freeze X movement when the strong player stops touching it
        if (collision.gameObject.CompareTag("StrongPlayer"))
        {
            rb.constraints |= RigidbodyConstraints2D.FreezePositionX;
        }
    }
}