using UnityEngine;

// Marks where the level ends (the flag). Optional: when the scene has none, LevelEnd uses the flag tile at the right-most
// end of Tilemap_Decorations. To place the goal yourself, add an empty object with this component (and a trigger
// Collider2D if you want your own size; otherwise a 3 x 4 zone is made around the object).
public class LevelGoal : MonoBehaviour
{
    [Tooltip("Seconds Kris has to reach the flag in to win. Shown as the countdown at the top left.")]
    public float timeLimit = 240f;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(transform.position, new Vector3(3f, 4f, 0f));
    }
}
