using System;
using UnityEngine;

// The pictures the level won celebration dances with (Assets/Resources/CelebrationArt.asset), gathered in one asset so the game can
// load them by name at runtime. The asset is made by CelebrationArtSetup (it runs by itself when Unity opens the project; the menu
// Tools > Setup Celebration Art redoes it). The pictures themselves stay where they are: the dance frames of Kris, Susie and
// Ralsei in Assets/Characters/Dance, Noelle's own pictures in Assets/Characters/Noelle.
public class CelebrationArt : ScriptableObject
{
    [Serializable]
    public class DanceSet
    {
        public Sprite[] dance;       // the dance frames, played in a loop (Kris, Susie, Ralsei)
        public Sprite[] idle;        // without dance frames (Noelle): standing
        public Sprite[] move;        //   the shuffle between the hops
        public Sprite[] jump;        //   in the air
        public Sprite[] special;     //   a dance move of its own (her arms), empty = use `move`

        public bool HasDance => Has(dance);
        public bool Complete => HasDance || (Has(idle) && Has(move) && Has(jump));
    }

    public DanceSet kris = new DanceSet(), susie = new DanceSet(), ralsei = new DanceSet(), noelle = new DanceSet();

    public bool Complete => kris.Complete && susie.Complete && ralsei.Complete && noelle.Complete;

    static bool Has(Sprite[] sprites)
    {
        if (sprites == null || sprites.Length == 0) return false;
        foreach (var s in sprites) if (s == null) return false;
        return true;
    }
}
