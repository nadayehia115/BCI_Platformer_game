using UnityEngine;
using UnityEngine.Events;

public class Obstacle : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Which helper can solve this obstacle")]
    [SerializeField] private HelperCharacter solvedBy;

    [Tooltip("Where the helper appears. Leave empty to use this object's position")]
    [SerializeField] private Transform helperSpot;

    [Header("When solved")]
    [Tooltip("Hook up what happens here, e.g. hide the water and show the ice")]
    [SerializeField] private UnityEvent onSolved;

    public HelperCharacter SolvedBy => solvedBy;
    public bool IsSolved { get; private set; }
    public Vector3 HelperPosition => helperSpot != null ? helperSpot.position : transform.position;

    public void Solve()
    {
        if (IsSolved) return;

        IsSolved = true;
        onSolved.Invoke();
    }
}
