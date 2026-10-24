using System.Collections;
using UnityEngine;

// A collectible gem: bobs and pulses while idle; when the player touches it, it adds to the score,
// pops and disappears. Needs a trigger Collider2D on this object (the player has a Rigidbody2D).
public class ScorePoint : MonoBehaviour
{
    [Header("Score")]
    [SerializeField] int value = 1;
    [Tooltip("Seconds added to the level clock when this gem is collected.")]
    [SerializeField] float timeBonus = 0.1f;

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
    [Tooltip("Leave empty to use the jingle in Resources/Audio/score_collect (tools/generate_score_sound.py makes it).")]
    [SerializeField] AudioClip collectSound;
    [Range(0f, 1f)] [SerializeField] float collectVolume = 0.8f;

    const string DefaultSoundResource = "Audio/score_collect";
    static AudioClip defaultSound;

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
        if (LevelEnd.Instance != null) LevelEnd.Instance.AddTime(timeBonus);
        PlayCollectSound();
        StartCoroutine(PopAndDisappear());
    }

    // The jingle follows the sound effects volume and the mute of the menu settings (AudioManager). When the game is started from
    // GameScene directly there is no AudioManager: a plain 2D sound is played instead.
    void PlayCollectSound()
    {
        var clip = collectSound;
        if (clip == null)
        {
            if (defaultSound == null) defaultSound = Resources.Load<AudioClip>(DefaultSoundResource);
            clip = defaultSound;
        }
        if (clip == null) return;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx(clip);
            return;
        }
        var go = new GameObject("Gem sound");                                    // the gem itself is gone a moment later
        var source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.PlayOneShot(clip, collectVolume);
        Destroy(go, clip.length + 0.1f);
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
