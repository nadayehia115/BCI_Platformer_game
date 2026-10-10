using UnityEngine;

// Shrinks the menu UI (never enlarges it) when the canvas is smaller than the layout was designed for,
// so panels and buttons never spill off the screen on narrow aspect ratios.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class UIFit : MonoBehaviour
{
    [SerializeField] Vector2 designSize = new Vector2(1700f, 1080f);

    RectTransform rt;
    RectTransform parent;

    void LateUpdate()
    {
        if (rt == null) rt = (RectTransform)transform;
        if (parent == null) parent = rt.parent as RectTransform;
        if (parent == null) return;

        Vector2 area = parent.rect.size;
        if (area.x <= 0f || area.y <= 0f) return;
        float scale = Mathf.Min(1f, area.x / designSize.x, area.y / designSize.y);
        rt.localScale = new Vector3(scale, scale, 1f);
    }
}
