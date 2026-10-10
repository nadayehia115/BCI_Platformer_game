using UnityEngine;
using UnityEngine.UI;

// Plays a list of sprites on a UI Image as a looping animation (unscaled time, so it also runs while paused).
[RequireComponent(typeof(Image))]
public class SpriteFlipbook : MonoBehaviour
{
    [SerializeField] Sprite[] frames;
    [SerializeField] float fps = 8f;
    [SerializeField] float timeOffset;

    Image image;

    void Awake()
    {
        image = GetComponent<Image>();
        if (frames != null && frames.Length > 0) image.sprite = frames[0];
    }

    void Update()
    {
        if (frames == null || frames.Length == 0) return;
        int i = Mathf.FloorToInt((Time.unscaledTime + timeOffset) * fps) % frames.Length;
        image.sprite = frames[i];
    }
}
