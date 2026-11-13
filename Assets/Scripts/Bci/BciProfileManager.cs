using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The onboarding popup that PLAY opens (MenuNavigation.Play calls TryOpen): a box for the Username, the NEW USER and OLD USER
// buttons and a small EXIT.
//
// When one of the user buttons is clicked it
//   * reads the Username and the screen (width, height, refresh rate),
//   * works out the pad frequency (a whole, even number of frames that divides the refresh rate exactly, outside the alpha band
//     and the epilepsy range) and the pad diameter, RoundToInt(7 * height / 1080)   (see BciProfile),
//   * makes the ProfileID  Username_WIDTHxHEIGHT_REFRESHHz,
//   * refuses a NEW USER whose name already has a model ("username used already, try another one"),
//   * writes ActiveProfile.json to the Documents folder and starts the flow (BciFlow.Begin).
public class BciProfileManager : MonoBehaviour
{
    static BciProfileManager open;

    TMP_InputField input;
    TMP_Text message;
    Canvas canvas;

    // Returns true when the popup took over (PLAY must not load the game itself).
    public static bool TryOpen()
    {
        if (BciSettings.Instance.SkipBciFlow) return false;
        if (open != null) return true;
        var go = new GameObject("BCI Profile Popup");
        open = go.AddComponent<BciProfileManager>();
        open.Build();
        return true;
    }

    void Build()
    {
        canvas = BciUi.NewCanvas("BCI Profile Canvas", 300, transform);
        var root = canvas.transform;

        var dim = BciUi.Panel("Dim", root, new Vector2(4000f, 3000f), new Color(0f, 0f, 0.05f, 0.75f), Vector2.zero);
        dim.raycastTarget = true;                                              // the menu below must not be clicked

        BciUi.Panel("Frame", root, new Vector2(1060f, 700f), Color.black, Vector2.zero);
        BciUi.Panel("Panel", root, new Vector2(1040f, 680f), BciUi.Cream, Vector2.zero);

        BciUi.Text("Title", root, "WHO IS PLAYING?", 80f, new Vector2(0f, 260f), new Vector2(950f, 100f), BciUi.Ink);
        input = BciUi.NewInput(root, new Vector2(0f, 150f), new Vector2(760f, 100f), "Username", 20);
        message = BciUi.Text("Message", root, "", 40f, new Vector2(0f, 60f), new Vector2(980f, 60f), BciUi.Danger);

        BciUi.NewButton("NEW USER", root, new Vector2(-250f, -70f), new Vector2(440f, 150f), 62f, () => Confirm(true));
        BciUi.NewButton("OLD USER", root, new Vector2(250f, -70f), new Vector2(440f, 150f), 62f, () => Confirm(false));
        BciUi.NewButton("EXIT", root, new Vector2(0f, -250f), new Vector2(260f, 84f), 44f, Close);

        input.ActivateInputField();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    void Close()
    {
        open = null;
        Destroy(gameObject);
    }

    void Confirm(bool isNewUser)
    {
        string typed = input.text.Trim();
        if (typed.Length == 0)
        {
            Warn("type a username first");
            return;
        }
        string name = BciProfile.SanitizeName(typed);
        if (name.Length == 0)
        {
            Warn("please use letters or numbers in the username");
            return;
        }

        var profile = BciProfile.Create(name, isNewUser);
        if (isNewUser && (BciProfile.UsernameTaken(name) || profile.ModelExists))
        {
            Warn("username used already, try another one");
            return;
        }
        if (!isNewUser && !profile.ModelExists)
        {
            Warn("no saved model for this username on this screen - choose NEW USER");
            return;
        }

        Debug.Log($"BCI: {(isNewUser ? "new" : "old")} user '{profile.Username}', {profile.ScreenWidth}x{profile.ScreenHeight} at {profile.RefreshRate:0.##} Hz, " +
                  $"pad {profile.TrueFrequency:0.##} Hz ({profile.FrameCount} frames), diameter {profile.PadDiameterSize}, id {profile.ProfileID}");
        open = null;
        BciFlow.Begin(profile);
    }

    void Warn(string text)
    {
        message.text = text;
        input.ActivateInputField();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }
}
