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
    bool shown;
    int lastShown = -1;          // the character that was brought in most recently

    // true while the highlighted character has been brought into the level with Z
    public bool IsShown => shown;

    void Start()
    {
        Select(0);
        ShowSelected(false);         // nobody is in the level until the player confirms with Z
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // One button, like the Python T key and the BCI flicker pad: each press sends whoever is out away and brings the
        // next character in (Ralsei -> Noelle -> Susie -> Ralsei ...).
        if (kb.digit1Key.wasPressedThisFrame) Advance();
    }

    // First press brings the highlighted character into the game scene, the next press makes it disappear again.
    public void Confirm() => ShowSelected(!shown);

    // One-button version (the Python "switch" command, one flicker pad): every press sends whoever is out away and brings the
    // next character in, Ralsei -> Noelle -> Susie -> Ralsei ... The first press shows the highlighted character.
    public void Advance()
    {
        int count = slots.Length;
        if (count == 0) return;
        if (!shown && lastShown < 0)
        {
            ShowSelected(true);
            return;
        }
        Select((lastShown >= 0 ? lastShown : Current) + 1);
        ShowSelected(true);
    }

    // At most one character is visible in the level, and only the highlighted one.
    void ShowSelected(bool show)
    {
        shown = show;
        if (show) lastShown = Current;
        for (int i = 0; i < characters.Length; i++)
            if (characters[i] != null) characters[i].SetActive(show && i == Current);
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
        }
    }
}
