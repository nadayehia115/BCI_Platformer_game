using System.Collections;
using UnityEngine;

// A collectible gem: bobs and pulses while idle; when the player touches it, it adds to the score,
// pops and disappears. Needs a trigger Collider2D on this object (the player has a Rigidbody2D).
public class ScorePoint : MonoBehaviour
{
    [Header("Score")]
    [SerializeField] int value = 1;

    [Header("Who can collect it")]
    [Tooltip("Tag of the player object. A Rigidbody2D that also has a PlayerController counts even if untagged.")]
    [SerializeField] string playerTag = "Player";

    [Header("Idle animation")]
    [SerializeField] Transform visual;
    [SerializeField] float bobHeight = 0.12f;
    [SerializeField] float bobSpeed = 2.5f;
    [SerializeField] float pulseAmount = 0.06f;
    [SerializeField] bool randomisePhase = true;

    [Header("Pickup")]
    [SerializeField] float popTime = 0.18f;
    [SerializeField] float popScale = 1.6f;
    [SerializeField] AudioClip collectSound;
    [Range(0f, 1f)] [SerializeField] float collectVolume = 0.8f;

    Vector3 visualStart;
    Vector3 visualScale;
    float phase;
    bool collected;

    void Awake()
    {
        if (visual == null) visual = transform;
        visualStart = visual.localPosition;
        visualScale = visual.localScale;
        phase = randomisePhase ? Random.value * Mathf.PI * 2f : 0f;
    }

    void Update()
    {
        if (collected) return;
        float t = Time.time * bobSpeed + phase;
        visual.localPosition = visualStart + Vector3.up * (Mathf.Sin(t) * bobHeight);
        visual.localScale = visualScale * (1f + Mathf.Sin(t * 2f) * pulseAmount);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || !IsPlayer(other)) return;
        collected = true;

        foreach (var c in GetComponents<Collider2D>()) c.enabled = false;
        ScoreManager.Add(value);
        if (collectSound != null) AudioSource.PlayClipAtPoint(collectSound, transform.position, collectVolume);
        StartCoroutine(PopAndDisappear());
    }

    bool IsPlayer(Collider2D other)
    {
        if (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag)) return true;
        var body = other.attachedRigidbody;
        return body != null && body.GetComponent("PlayerController") != null;
    }

    IEnumerator PopAndDisappear()
    {
        var sprite = visual.GetComponent<SpriteRenderer>();
        Color start = sprite != null ? sprite.color : Color.white;
        for (float t = 0f; t < popTime; t += Time.deltaTime)
        {
            float k = t / popTime;
            visual.localScale = visualScale * Mathf.Lerp(1f, popScale, k);
            if (sprite != null) sprite.color = new Color(start.r, start.g, start.b, 1f - k);
            yield return null;
        }
        Destroy(gameObject);
    }
}
