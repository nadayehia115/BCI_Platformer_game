using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CharacterSwitcher : MonoBehaviour
{
    [SerializeField] Image[] slots = new Image[3];
    [SerializeField] GameObject[] characters = new GameObject[3];
    [SerializeField] float selectedScale = 1.3f;
    [SerializeField] Color selectedColor = Color.white;
    [SerializeField] Color unselectedColor = new Color(1f, 1f, 1f, 0.55f);

    public int Current { get; private set; }

    void Start() => Select(0);

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.eKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) Select(Current + 1);
        else if (kb.qKey.wasPressedThisFrame) Select(Current - 1);
        else if (kb.digit1Key.wasPressedThisFrame) Select(0);
        else if (kb.digit2Key.wasPressedThisFrame) Select(1);
        else if (kb.digit3Key.wasPressedThisFrame) Select(2);
    }

    public void Select(int index)
    {
        int count = slots.Length;
        if (count == 0) return;
        Current = ((index % count) + count) % count;

        for (int i = 0; i < count; i++)
        {
            bool active = i == Current;
            if (slots[i] != null)
            {
                slots[i].transform.localScale = Vector3.one * (active ? selectedScale : 1f);
                slots[i].color = active ? selectedColor : unselectedColor;
            }
            if (i < characters.Length && characters[i] != null)
                characters[i].SetActive(active);
        }
    }
}
