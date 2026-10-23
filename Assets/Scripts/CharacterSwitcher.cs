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

    int current;
    bool shown;
    int lastShown = -1;          // the character that was brought in most recently

    // The HUD prefab has a second, empty copy of this component (on Slot1: no portraits, only Ralsei) and the scene's Noelle
    // script points at that copy. A copy without portraits hands everything over to the real switcher on the panel above it, so
    // there is one state (S, the Python "switch" and every power script see the same selection).
    CharacterSwitcher real;

    public int Current => real != null ? real.Current : current;

    // true while the highlighted character has been brought into the level with Z
    public bool IsShown => real != null ? real.IsShown : shown;

    public Image[] Slots => real != null ? real.Slots : slots;                // the portraits (the SSVEP pads go under them)

    void Awake()
    {
        foreach (var s in slots)
            if (s != null) return;                                            // it has portraits: this is the real one
        foreach (var other in GetComponentsInParent<CharacterSwitcher>(true))
            if (other != this) { real = other; return; }
        foreach (var other in FindObjectsByType<CharacterSwitcher>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (other != this) { real = other; return; }
    }

    void Start()
    {
        if (real != null) return;
        Select(0);
        ShowSelected(false);         // nobody is in the level until the player confirms with Z
    }

    void Update()
    {
        if (real != null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // The same keys as in Python: S starts the dark fountain at the obstacle (then the pad appears), T = the classification says
        // the player kept the focus on the pad: the friend the obstacle needs is brought in by himself.
        if (kb.sKey.wasPressedThisFrame) KrisForms.Request();
        if (kb.tKey.wasPressedThisFrame) AutoSelect();
    }

    // First press brings the highlighted character into the game scene, the next press makes it disappear again.
    public void Confirm()
    {
        if (real != null) { real.Confirm(); return; }
        if (shown) ShowSelected(false);                                // sending a friend away is always allowed: they do it themselves when done
        else if (KrisForms.PowersUnlocked) ShowSelected(true);         // bringing one in needs the pad (S, then T)
    }

    // The Python "switch" message (its T key) and the T key: the classification has shown that the player kept the focus on the pad,
    // so the power the nearest obstacle needs is brought in: water -> Noelle, a tree -> Ralsei, a box -> Susie (see PowerTargets).
    // It only works once the fountain has played at the obstacle (S) and the pad is out, and it uses the pad up. With no obstacle in
    // sight nothing happens. Returns whether a friend was brought in (or is already at work).
    public bool AutoSelect()
    {
        if (real != null) return real.AutoSelect();
        if (!KrisForms.PowersUnlocked)
        {
            Debug.Log("The pad is not out: press S at the obstacle first (the dark fountain plays, then the pad appears).");
            return false;
        }
        if (!PowerTargets.Nearest(true, 0f, out var target))
        {
            Debug.Log("No obstacle in sight needs a friend's power.");
            return false;
        }
        if (!(shown && current == target.power))                    // (already out and at work on it: nothing to do)
        {
            Select(target.power);
            ShowSelected(true);
        }
        KrisForms.Disarm();                                         // the pad is used up: the next obstacle starts with S again
        return true;
    }

    // The old one-button version: every press sends whoever is out away and brings the next character in, Ralsei -> Noelle ->
    // Susie -> Ralsei ... The first press shows the highlighted character. (S and the Python switch use AutoSelect now.)
    public void Advance()
    {
        if (real != null) { real.Advance(); return; }
        if (!KrisForms.PowersUnlocked) return;            // Kris is still in the light world: the friends' powers are locked
        int count = slots.Length;
        if (count == 0) return;
        if (!shown && lastShown < 0)
        {
            ShowSelected(true);
            return;
        }
        Select((lastShown >= 0 ? lastShown : current) + 1);
        ShowSelected(true);
    }

    // At most one character is visible in the level, and only the highlighted one.
    void ShowSelected(bool show)
    {
        shown = show;
        if (show) lastShown = current;
        for (int i = 0; i < characters.Length; i++)
            if (characters[i] != null) characters[i].SetActive(show && i == current);
    }

    public void Select(int index)
    {
        if (real != null) { real.Select(index); return; }
        int count = slots.Length;
        if (count == 0) return;
        current = ((index % count) + count) % count;

        for (int i = 0; i < count; i++)
        {
            bool active = i == current;
            if (slots[i] != null)
            {
                slots[i].transform.localScale = Vector3.one * (active ? selectedScale : 1f);
                slots[i].color = active ? selectedColor : unselectedColor;
            }
        }
    }
}
