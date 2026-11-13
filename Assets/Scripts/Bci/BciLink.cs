using UnityEngine;

// What the game tells the live classifier (bci_game.py, UDP): the flickering pad is on the screen / gone. Only while a BCI profile is
// active (the flow was started from the menu); the Python side decides whether the player is focusing, and answers with the UDP
// "switch" that BciListener turns into CharacterSwitcher.AutoSelect (the helper arrives).
public static class BciLink
{
    static bool Active => BciProfile.Active != null;

    public static void PadOn()
    {
        if (Active) BciUdp.Send("pad|on", BciSettings.Instance.ClassifierPort);
    }

    public static void PadOff()
    {
        if (Active) BciUdp.Send("pad|off", BciSettings.Instance.ClassifierPort);
    }
}
