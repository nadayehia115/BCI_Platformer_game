using System.Collections;
using UnityEngine;

// Put this on each helper prefab (Noelle, Ralsei, Susie).
// If the prefab has an Animator, its default animation plays automatically when the helper appears.
public class HelperActor : MonoBehaviour
{
    [Tooltip("Seconds between appearing and using the power")]
    [SerializeField] private float timeBeforePower = 0.6f;

    [Tooltip("Seconds the helper stays after using the power, before disappearing")]
    [SerializeField] private float timeAfterPower = 0.8f;

    public IEnumerator Perform(Obstacle target)
    {
        yield return new WaitForSeconds(timeBeforePower);

        target.Solve();

        yield return new WaitForSeconds(timeAfterPower);

        Destroy(gameObject);
    }
}
