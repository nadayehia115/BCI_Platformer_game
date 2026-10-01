using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class PlayerJump : MonoBehaviour
{
    [SerializeField] private Rigidbody2D rigidBody;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float jumpForce = 6;
    private float playerHalfHight;

    private void Start()
    {
        playerHalfHight = spriteRenderer.bounds.extents.y;
    }
    
    // Update is called once per frame
    void Update()
    {

        if (Input.GetKeyDown(KeyCode.W)&& GetisGrounded())
        {
            Jump();
        }
    }

    private bool GetisGrounded()
    {
        return Physics2D.Raycast(transform.position, Vector2.down, playerHalfHight + 0.1f, LayerMask.GetMask("Ground"));

    }
   
    private void Jump()
    {
        rigidBody.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }

}
