using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Stage 2: "Checking headset connectivity...". A top-down picture of the head with the 8 electrodes of the Unicorn. Their colours
// come from file 1 (UDP payload  status|Fz=GOOD,C3=NOISY,...): green = GOOD, yellow = NOISY, red = FLATLINE / RAILED / NO DATA.
// PROCEED works only when all 8 are green. The character comments on what is wrong.
public class BciHeadsetStage : BciStage
{
    static readonly string[] Names = { "Fz", "C3", "Cz", "C4", "Pz", "PO7", "Oz", "PO8" };
    // where the electrode gems sit in the picture (pixels, y from the top; the picture is 304 x 382)
    static readonly Vector2[] PicturePos =
    {
        new Vector2(160.8f, 116.9f), new Vector2(103.2f, 174.8f), new Vector2(161.1f, 162.1f), new Vector2(218.2f, 174.9f),
        new Vector2(160.8f, 200.5f), new Vector2(109.7f, 239.0f), new Vector2(160.8f, 245.0f), new Vector2(212.1f, 238.8f),
    };
    static readonly Vector2 PictureSize = new Vector2(304f, 382f);
    static readonly Vector2 BrainCentre = new Vector2(162f, 182f);       // the middle of the brain itself in the picture
    const float Scale = 3.0f;

    static readonly Color Green = new Color(0.45f, 1f, 0.25f), Yellow = new Color(1f, 0.88f, 0.1f), Red = new Color(1f, 0.2f, 0.2f),
                          Waiting = new Color(0.45f, 0.45f, 0.5f);

    enum Mood { Connecting, Error, Red, Yellow, Green }

    readonly Image[] gems = new Image[8];
    readonly Color[] target = new Color[8];
    Button proceed;
    BciSpeechBubble bubble;
    Mood shownMood = (Mood)(-1), wantedMood;
    float wantedSince;

    protected override void BuildScreen(Transform root)
    {
        BciUi.Text("Title", root, "Checking headset connectivity...", 76f, new Vector2(0f, 470f), new Vector2(1500f, 100f), Color.white);

        var brainSprite = Resources.Load<Sprite>("Bci/brain");
        var gemSprite = Resources.Load<Sprite>("Bci/gem");

        var brain = BciUi.Rect("Brain", root);
        brain.sizeDelta = PictureSize * Scale;
        brain.pivot = new Vector2(BrainCentre.x / PictureSize.x, 1f - BrainCentre.y / PictureSize.y);
        brain.anchoredPosition = new Vector2(360f, -5f);                  // right of the middle: the speech bubble has the left side
        var brainImage = brain.gameObject.AddComponent<Image>();
        brainImage.sprite = brainSprite;
        brainImage.raycastTarget = false;

        for (int i = 0; i < Names.Length; i++)
        {
            var offset = new Vector2((PicturePos[i].x - BrainCentre.x) * Scale, -(PicturePos[i].y - BrainCentre.y) * Scale);
            var gemRect = BciUi.Rect("Electrode " + Names[i], brain);
            gemRect.anchorMin = gemRect.anchorMax = brain.pivot;
            gemRect.sizeDelta = gemSprite != null ? new Vector2(gemSprite.rect.width, gemSprite.rect.height) * Scale : new Vector2(90f, 90f);
            gemRect.anchoredPosition = offset;
            gems[i] = gemRect.gameObject.AddComponent<Image>();
            gems[i].sprite = gemSprite;
            gems[i].raycastTarget = false;
            gems[i].color = Waiting;
            target[i] = Waiting;

            // the four on the middle line are close to each other: their names go to the right of the gem, the others under it
            bool middle = PicturePos[i].x > 150f && PicturePos[i].x < 175f;
            var label = BciUi.Text("Name", gemRect, Names[i], 34f, middle ? new Vector2(80f, 4f) : new Vector2(0f, -78f), new Vector2(140f, 44f),
                                   Color.white, middle ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
        }

        proceed = BciUi.NewButton("PROCEED", root, new Vector2(730f, -440f), new Vector2(400f, 110f), 54f, BciFlow.AfterHeadsetCheck);
        proceed.interactable = false;

        bubble = BciSpeechBubble.Create(root, new Vector2(0f, 0f), new Vector2(40f, 40f), 640f);
        bubble.SetPose(BciSpeechBubble.Pose.Point);                       // Tenna points at the screen
        bubble.Say("Connecting to your headset...");
    }

    protected override void Update()
    {
        base.Update();
        if (gems[0] == null) return;

        // DEMO: the letter O goes on even when the electrodes are not all green
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.oKey.wasPressedThisFrame)
        {
            Debug.Log("BCI: O pressed - going on without waiting for all electrodes (demo).");
            BciFlow.AfterHeadsetCheck();
            return;
        }
        var manager = BciProcessManager.Instance;

        bool fresh = manager.Statuses.Count >= Names.Length && Time.unscaledTime - manager.LastStatusTime < 4f;
        bool anyRed = false, anyYellow = false, allGreen = true;
        for (int i = 0; i < Names.Length; i++)
        {
            if (manager.HeadsetError != null || manager.Statuses.Count == 0)
                target[i] = Waiting;
            else
            {
                string status = fresh && manager.Statuses.TryGetValue(Names[i], out var s) ? s : "NO DATA";
                switch (status)
                {
                    case "GOOD": target[i] = Green; break;
                    case "NOISY": target[i] = Yellow; anyYellow = true; break;
                    default: target[i] = Red; anyRed = true; break;                // FLATLINE, RAILED, NO DATA
                }
            }
            if (target[i] != Green) allGreen = false;
            gems[i].color = Color.Lerp(gems[i].color, target[i], 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        }

        proceed.interactable = allGreen && fresh && manager.HeadsetError == null;

        Mood mood = manager.HeadsetError != null ? Mood.Error
                  : manager.Statuses.Count == 0 ? Mood.Connecting
                  : anyRed ? Mood.Red
                  : anyYellow ? Mood.Yellow : Mood.Green;
        if (mood != wantedMood)
        {
            wantedMood = mood;
            wantedSince = Time.unscaledTime;
        }
        if (wantedMood != shownMood && Time.unscaledTime - wantedSince > 0.8f)         // a change must last a moment before the character says it
        {
            shownMood = wantedMood;
            bubble.Say(MessageFor(shownMood, manager.HeadsetError));
        }
    }

    static string MessageFor(Mood mood, string error)
    {
        switch (mood)
        {
            case Mood.Error:
                return "I can't reach the headset (" + error + "). Please switch it on, plug in the dongle and let's try again.";
            case Mood.Red:
                return "The electrode is not detecting any brain waves. Please adjust the headset till the icon turns green.";
            case Mood.Yellow:
                return "It seems there is noise not allowing me to read your brain clearly, please stay still.";
            case Mood.Green:
                return "Perfect! I can read your mind loud and clear. Let's go!";
            default:
                return "Connecting to your headset...";
        }
    }
}
