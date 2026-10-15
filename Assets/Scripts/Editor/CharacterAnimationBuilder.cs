using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > Build Character Animations
// Turns the frames in Assets/Characters/<Name>/ (made by tools/generate_character_animations.py) into
// Idol / Run / jump / fall clips and an Animator Controller for Noelle, Ralsei and Susie.
// Each controller is a copy of Kris's (_PlayerAnim) so it uses the same parameters and transitions
// (IsRunning, IsGrounded, YVelocity); only the animations are swapped.
public static class CharacterAnimationBuilder
{
    const string KrisController = "Assets/Animation/_PlayerAnim.controller";

    // Every character uses 16 pixels per unit, the same as Kris, so all the pixel art has the same pixel size.
    // (Noelle's art has about 3x the resolution of Kris's, so at 16 PPU she is drawn much taller than the others.)
    const int Ppu = 16;
    const float RActionSheetScale = 1.55f;       // how much bigger Ralsei's R-Action sheet is drawn than his idle frames
    static readonly (string name, int ppu)[] Characters = { ("Noelle", Ppu), ("Ralsei", Ppu), ("Susie", Ppu) };

    [MenuItem("Tools/Build Character Animations")]
    static void Build()
    {
        if (!File.Exists(KrisController))
        {
            Debug.LogError("Kris's controller not found at " + KrisController);
            return;
        }

        AssetDatabase.Refresh();
        foreach (var (name, ppu) in Characters) BuildCharacter(name, ppu);
        AssetDatabase.SaveAssets();
        Debug.Log("Character animations built for Noelle, Ralsei and Susie (Assets/Characters/<Name>/).");
    }

    static void BuildCharacter(string name, int ppu)
    {
        string folder = "Assets/Characters/" + name;
        if (!Directory.Exists(folder))
        {
            Debug.LogError("Missing " + folder + " - run tools/generate_character_animations.py first.");
            return;
        }

        foreach (var file in Directory.GetFiles(folder, "*.png")) ImportSprite(file.Replace('\\', '/'), ppu);

        var idle = Frames(folder, name, "idle");
        var run = Frames(folder, name, "run");
        var jump = Frames(folder, name, "jump");
        var fall = Frames(folder, name, "fall");

        var idleClip = MakeClip(folder + "/" + name + "_Idol.anim", idle, 8f, true);
        var runClip = MakeClip(folder + "/" + name + "_Run.anim", run, 12f, true);
        var jumpClip = MakeClip(folder + "/" + name + "_jump.anim", jump, 10f, false);
        var fallClip = MakeClip(folder + "/" + name + "_fall.anim", fall, 10f, false);

        string controllerPath = folder + "/" + name + "_Anim.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        AssetDatabase.CopyAsset(KrisController, controllerPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        foreach (var child in controller.layers[0].stateMachine.states)
        {
            switch (child.state.name.ToLowerInvariant())
            {
                case "idol":
                case "idle": child.state.motion = idleClip; break;
                case "run": child.state.motion = runClip; break;
                case "jump": child.state.motion = jumpClip; break;
                case "fall": child.state.motion = fallClip; break;
            }
        }
        if (name == "Ralsei") AddRAction(controller, folder, ppu, idle[0]);
        EditorUtility.SetDirty(controller);
    }

    // Ralsei's R-Action: he wraps his scarf around himself and a hookable object and becomes a platform.
    // Plays once and holds on the last frame while the bool parameter IsPlatform is true; then back to idle.
    static void AddRAction(AnimatorController controller, string folder, int ppu, Sprite idleSprite)
    {
        string dir = folder + "/RAction";
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "Ralsei_wrap_*.png")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_').Last()))
            .ToArray();
        if (files.Length == 0) return;

        // These frames sit on a bigger canvas than the idle ones. Import once to learn the canvas height, then set a
        // pivot that keeps his feet at the same height as the idle sprite when the animator switches between them.
        // The R-Action sheet is drawn ~1.55x larger than Ralsei's idle frames (his head is ~1.55x wider there), so these
        // frames are imported at a proportionally higher PPU to come out the same size on screen.
        int wrapPpu = Mathf.RoundToInt(ppu * RActionSheetScale);
        foreach (var f in files) ImportSprite(f, wrapPpu);
        float frameHeight = AssetDatabase.LoadAssetAtPath<Sprite>(files[0]).rect.height;
        // feet below the pivot in world units must equal the idle sprite's: (idleH / 2) / ppu  ==  pivotY * frameHeight / wrapPpu
        float pivotY = (idleSprite.rect.height / 2f) * wrapPpu / ppu / frameHeight;
        foreach (var f in files) ImportSprite(f, wrapPpu, pivotY);
        var frames = files.Select(f => AssetDatabase.LoadAssetAtPath<Sprite>(f)).ToArray();

        var clip = MakeClip(folder + "/Ralsei_RAction.anim", frames, 8f, false);

        if (controller.parameters.All(p => p.name != "IsPlatform"))
            controller.AddParameter("IsPlatform", AnimatorControllerParameterType.Bool);

        var machine = controller.layers[0].stateMachine;
        foreach (var existing in machine.states.Where(s => s.state.name == "RAction").ToArray())
            machine.RemoveState(existing.state);

        var state = machine.AddState("RAction");
        state.motion = clip;

        var toAction = machine.AddAnyStateTransition(state);
        toAction.hasExitTime = false;
        toAction.duration = 0f;
        toAction.canTransitionToSelf = false;
        toAction.AddCondition(AnimatorConditionMode.If, 0, "IsPlatform");

        var idleState = machine.states.FirstOrDefault(s => s.state.name.ToLowerInvariant() == "idol").state;
        if (idleState != null)
        {
            var back = state.AddTransition(idleState);
            back.hasExitTime = false;
            back.duration = 0.1f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0, "IsPlatform");
        }
    }

    static void ImportSprite(string path, int ppu, float pivotY = 0.5f)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.spritePixelsPerUnit = ppu;
        var settings = new TextureImporterSettings();
        imp.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)(Mathf.Approximately(pivotY, 0.5f) ? SpriteAlignment.Center : SpriteAlignment.Custom);
        imp.SetTextureSettings(settings);
        imp.spritePivot = new Vector2(0.5f, pivotY);
        imp.SaveAndReimport();
    }

    static Sprite[] Frames(string folder, string name, string kind)
    {
        return Directory.GetFiles(folder, name + "_" + kind + "_*.png")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('_').Last()))
            .Select(f => AssetDatabase.LoadAssetAtPath<Sprite>(f))
            .ToArray();
    }

    static AnimationClip MakeClip(string path, Sprite[] frames, float fps, bool loop)
    {
        AssetDatabase.DeleteAsset(path);
        var clip = new AnimationClip { frameRate = fps };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }
}
