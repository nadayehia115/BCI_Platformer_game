using UnityEngine;

// Scales the background art so it always covers the canvas, at any aspect ratio.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class ArtRootFit : MonoBehaviour
{
    [SerializeField] Vector2 artSize = new Vector2(1944f, 1080f);

    RectTransform rt;
    RectTransform parent;

    void LateUpdate()
    {
        if (rt == null) rt = (RectTransform)transform;
        if (parent == null) parent = rt.parent as RectTransform;
        if (parent == null) return;

        Vector2 area = parent.rect.size;
        if (area.x <= 0f || area.y <= 0f) return;
        float scale = Mathf.Max(area.x / artSize.x, area.y / artSize.y);
        rt.localScale = new Vector3(scale, scale, 1f);
    }
}
