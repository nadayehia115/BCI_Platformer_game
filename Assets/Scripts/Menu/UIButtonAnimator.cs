using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover / press animation and UI sounds for a button. Uses unscaled time so it also works while paused.
[RequireComponent(typeof(RectTransform))]
public class UIButtonAnimator : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] float hoverScale = 1.08f;
    [SerializeField] float pressedScale = 0.94f;
    [SerializeField] float speed = 14f;
    [SerializeField] bool isBackButton;
    [SerializeField] bool playHoverSound = true;

    Vector3 baseScale;
    float target = 1f;
    bool hovering, pressed;

    void Awake() => baseScale = transform.localScale;

    void OnEnable()
    {
        transform.localScale = baseScale;
        hovering = pressed = false;
        target = 1f;
    }

    void Update()
    {
        Vector3 goal = baseScale * target;
        transform.localScale = Vector3.Lerp(transform.localScale, goal, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
    }

    bool Interactable()
    {
        var s = GetComponent<Selectable>();
        return s == null || s.IsInteractable();
    }

    void Refresh() => target = pressed ? pressedScale : hovering ? hoverScale : 1f;

    public void OnPointerEnter(PointerEventData e)
    {
        if (!Interactable()) return;
        hovering = true;
        Refresh();
        if (playHoverSound && AudioManager.Instance != null) AudioManager.Instance.PlayHover();
    }

    public void OnPointerExit(PointerEventData e) { hovering = pressed = false; Refresh(); }

    public void OnPointerDown(PointerEventData e)
    {
        if (!Interactable()) return;
        pressed = true;
        Refresh();
        if (AudioManager.Instance == null) return;
        if (isBackButton) AudioManager.Instance.PlayBack(); else AudioManager.Instance.PlayClick();
    }

    public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }

    // Keyboard / gamepad navigation highlights the button the same way.
    public void OnSelect(BaseEventData e) { hovering = true; Refresh(); }
    public void OnDeselect(BaseEventData e) { hovering = false; Refresh(); }
}
