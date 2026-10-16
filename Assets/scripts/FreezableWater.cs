using System.Collections;
using UnityEngine;

// Put this on the water obstacle (the same object that has the Obstacle script).
// In the Obstacle's "On Solved" event, call FreezableWater.Freeze().
public class FreezableWater : MonoBehaviour
{
    [Header("Water (before freezing)")]
    [SerializeField] private SpriteRenderer waterSprite;
    [Tooltip("The water's own collider (e.g. a trigger that hurts or slows Kris)")]
    [SerializeField] private Collider2D waterHazard;

    [Header("Ice (after freezing)")]
    [SerializeField] private SpriteRenderer iceSprite;
    [Tooltip("Solid collider Kris can walk on. Put the ice object on the Ground layer")]
    [SerializeField] private Collider2D iceCollider;

    [Header("Timing")]
    [SerializeField] private float freezeDuration = 0.8f;

    private bool frozen;

    private void Awake()
    {
        // Start in the unfrozen state
        if (iceCollider != null) iceCollider.enabled = false;
        SetAlpha(iceSprite, 0f);
    }

    public void Freeze()
    {
        if (frozen) return;

        frozen = true;
        StartCoroutine(FreezeRoutine());
    }

    private IEnumerator FreezeRoutine()
    {
        // The water stops being a hazard and the ice becomes solid right away
        if (waterHazard != null) waterHazard.enabled = false;
        if (iceCollider != null) iceCollider.enabled = true;

        // Fade the water out and the ice in
        float time = 0f;
        while (time < freezeDuration)
        {
            time += Time.deltaTime;
            float progress = Mathf.Clamp01(time / freezeDuration);

            SetAlpha(iceSprite, progress);
            SetAlpha(waterSprite, 1f - progress);

            yield return null;
        }

        SetAlpha(iceSprite, 1f);
        if (waterSprite != null) waterSprite.enabled = false;
    }

    private void SetAlpha(SpriteRenderer sprite, float alpha)
    {
        if (sprite == null) return;

        Color color = sprite.color;
        color.a = alpha;
        sprite.color = color;
    }
}
