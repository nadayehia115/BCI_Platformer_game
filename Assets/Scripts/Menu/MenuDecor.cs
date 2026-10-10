using UnityEngine;

// Small ambient animations for the menu background (all use unscaled time).
public class MenuDecor : MonoBehaviour
{
    public enum Mode { Bob, Drift }

    [SerializeField] Mode mode = Mode.Bob;
    [Header("Bob")]
    [SerializeField] float amplitude = 14f;
    [SerializeField] float period = 2.2f;
    [SerializeField] float phase;
    [Header("Drift (pixels / second, wraps inside the art width)")]
    [SerializeField] float speed = 20f;
    [SerializeField] float wrapWidth = 1944f;
    [SerializeField] float margin = 260f;

    RectTransform rt;
    Vector2 start;

    void Awake()
    {
        rt = (RectTransform)transform;
        start = rt.anchoredPosition;
    }

    // Used by the scene builder.
    public void Configure(Mode m, float amp, float per, float ph, float spd)
    {
        mode = m;
        amplitude = amp;
        period = per;
        phase = ph;
        speed = spd;
    }

    void Update()
    {
        if (mode == Mode.Bob)
        {
            float y = Mathf.Sin((Time.unscaledTime / period + phase) * Mathf.PI * 2f) * amplitude;
            rt.anchoredPosition = start + new Vector2(0f, y);
        }
        else
        {
            var p = rt.anchoredPosition;
            p.x += speed * Time.unscaledDeltaTime;
            if (p.x > wrapWidth + margin) p.x = -margin;
            rt.anchoredPosition = p;
        }
    }
}
