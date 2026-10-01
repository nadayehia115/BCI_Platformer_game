using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class PlayerMovement : MonoBehaviour
{
   [SerializeField] private float speed = 5f;
   [SerializeField] private SpriteRenderer spriteRenderer;
   [SerializeField] private Animator _animator;
   private Vector2 movement;

   private Vector2 screenBounds;

   private float playerHalfWidth;
   
   private float xPosLastFrame;

   private void Start()
   {
         screenBounds = Camera.main.ScreenToWorldPoint(new Vector2(Screen.width, Screen.height));
         playerHalfWidth = spriteRenderer.bounds.extents.x;
   }

    // Update is called once per frame
    void Update()
    {
        HandleMovement();
        ClampMovement();
        FlipCharacterx();
    }
    private void FlipCharacterx()
    {
        float input = Input.GetAxis("Horizontal");
        if (input > 0 && (transform.position.x > xPosLastFrame))
        {
            spriteRenderer.flipX = false;
        }
        else if (input < 0 && (transform.position.x < xPosLastFrame))
        {
            spriteRenderer.flipX = true;
        }
        xPosLastFrame = transform.position.x;
    }
    private void ClampMovement()
    {
        float clampedX = Mathf.Clamp(transform.position.x, -screenBounds.x + playerHalfWidth, screenBounds.x - playerHalfWidth);
        Vector2 pos = transform.position;
        pos.x = clampedX;
        transform.position = pos;
    }

    private void HandleMovement()
    {
        float input = Input.GetAxis("Horizontal");
        movement.x = input * speed * Time.deltaTime;
        transform.Translate(movement);
        if (input != 0)
        {
            _animator.SetBool("IsRunning", true);
        }
        else
        {
            _animator.SetBool("IsRunning", false);
        }
    }
}
