using System.Collections;
using UnityEngine;

// Put this on Kris.
public class HelperSummoner : MonoBehaviour
{
    [Header("Summoning")]
    [SerializeField] private KeyCode summonKey = KeyCode.E;
    [SerializeField] private float summonRadius = 2f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Helper prefabs (one per character)")]
    [SerializeField] private HelperActor noellePrefab;
    [SerializeField] private HelperActor ralseiPrefab;
    [SerializeField] private HelperActor susiePrefab;

    // The currently selected helper. Your icon-switching script should call SetSelected().
    public HelperCharacter Selected { get; private set; } = HelperCharacter.Noelle;

    private bool busy;

    public void SetSelected(HelperCharacter character)
    {
        Selected = character;
    }

    private void Update()
    {
        if (Input.GetKeyDown(summonKey) && !busy)
        {
            TrySummon();
        }
    }

    private void TrySummon()
    {
        // Invisible circle around Kris, like the ground check
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, summonRadius, obstacleLayer);

        Obstacle closestMatch = null;
        float closestDistance = float.MaxValue;
        bool foundAnyObstacle = false;
        HelperCharacter neededCharacter = Selected;

        foreach (Collider2D hit in hits)
        {
            Obstacle obstacle = hit.GetComponentInParent<Obstacle>();
            if (obstacle == null || obstacle.IsSolved) continue;

            foundAnyObstacle = true;
            neededCharacter = obstacle.SolvedBy;

            if (obstacle.SolvedBy != Selected) continue;

            float distance = ((Vector2)obstacle.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestMatch = obstacle;
                closestDistance = distance;
            }
        }

        if (closestMatch != null)
        {
            StartCoroutine(SummonRoutine(closestMatch));
        }
        else if (foundAnyObstacle)
        {
            // Wrong helper for this obstacle. Add a sound or icon shake here later.
            Debug.Log($"{Selected} can't solve this. It needs {neededCharacter}.");
        }
        else
        {
            Debug.Log("No obstacle nearby.");
        }
    }

    private IEnumerator SummonRoutine(Obstacle target)
    {
        busy = true;

        HelperActor prefab = GetPrefab(Selected);

        if (prefab == null)
        {
            Debug.LogWarning($"No prefab assigned for {Selected}. Solving the obstacle without an animation.");
            target.Solve();
        }
        else
        {
            HelperActor helper = Instantiate(prefab, target.HelperPosition, Quaternion.identity);
            yield return helper.Perform(target);
        }

        busy = false;
    }

    private HelperActor GetPrefab(HelperCharacter character)
    {
        switch (character)
        {
            case HelperCharacter.Noelle: return noellePrefab;
            case HelperCharacter.Ralsei: return ralseiPrefab;
            case HelperCharacter.Susie: return susiePrefab;
            default: return null;
        }
    }

    // Shows the summon circle in the Scene view when Kris is selected
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, summonRadius);
    }
}
