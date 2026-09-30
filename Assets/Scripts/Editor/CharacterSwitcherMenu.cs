using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class CharacterSwitcherMenu
{
    [MenuItem("Tools/Create Character Switcher HUD")]
    static void Create()
    {
        var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Character Switcher HUD");
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var panel = new GameObject("CharacterSwitcher", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(CharacterSwitcher));
        panel.transform.SetParent(canvasGo.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-30, -30);
        rect.sizeDelta = new Vector2(320, 120);

        var layout = panel.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 20;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var slots = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            var slot = new GameObject("Slot" + (i + 1), typeof(RectTransform), typeof(Image));
            slot.transform.SetParent(panel.transform, false);
            ((RectTransform)slot.transform).sizeDelta = new Vector2(80, 80);
            slots[i] = slot.GetComponent<Image>();
            slots[i].sprite = circle;
            slots[i].preserveAspect = true;
        }

        var so = new SerializedObject(panel.GetComponent<CharacterSwitcher>());
        var slotsProp = so.FindProperty("slots");
        for (int i = 0; i < 3; i++) slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        so.ApplyModifiedProperties();

        Selection.activeGameObject = panel;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
    }
}
