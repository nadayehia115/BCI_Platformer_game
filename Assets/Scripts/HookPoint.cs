using UnityEngine;

// Marks a place Ralsei's scarf can be hooked on: the tip of a tree branch. It is only a position (one per tree). They are
// created by Tools > Add Hook Points To Trees, and you can also add one by hand wherever something hookable is.
public class HookPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.7f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
