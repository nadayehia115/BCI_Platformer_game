using UnityEngine;

// Stage 1: monitor contrast calibration. Pure black, with a very dark grey square (RGB 5,5,5) in the middle that the player must
// be able to see; the character explains. (The headset script, file 1, has been started in the background meanwhile.)
public class BciContrastStage : BciStage
{
    protected override void BuildScreen(Transform root)
    {
        // RGB 0,0,0 behind (BciStage) and RGB 5,5,5 here: a difference that is only visible on a well adjusted monitor
        BciUi.Panel("Dark Grey Square", root, new Vector2(560f, 560f), new Color32(5, 5, 5, 255), new Vector2(0f, 60f));

        var bubble = BciSpeechBubble.Create(root, new Vector2(0f, 0f), new Vector2(50f, 40f), 1150f, 0.55f);
        bubble.Say("Let's tune your vision! Adjust your monitor's physical brightness until you can see the dark gray box hidden " +
                   "in the center. This helps me read your brain signals better!");

        BciUi.NewButton("CONFIRM", root, new Vector2(700f, -400f), new Vector2(420f, 120f), 56f, BciFlow.AfterContrast);
    }
}
