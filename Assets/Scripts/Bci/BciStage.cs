using UnityEngine;
using UnityEngine.InputSystem;

// The base of the BCI stage screens (each is one tiny scene with one component that builds the whole screen in code).
// Esc leaves the flow (every Python script stops, the main menu comes back).
public abstract class BciStage : MonoBehaviour
{
    protected Canvas canvas;
    protected BciSettings Settings => BciSettings.Instance;
    protected BciProfile Profile => BciProfile.Active;

    protected virtual void Awake()
    {
        Time.timeScale = 1f;
        Application.runInBackground = true;
        BciFlow.EnsureProfile();                                  // a stage scene opened directly (testing) gets a profile of its own
        canvas = BciUi.NewCanvas(GetType().Name + " Canvas", 10, transform);
        var back = BciUi.Rect("Black", canvas.transform);
        BciUi.Stretch(back);
        var image = back.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.black;                                // every BCI stage is on pure black
        BuildScreen(canvas.transform);
    }

    protected abstract void BuildScreen(Transform root);

    protected virtual void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && AllowEscape) BciFlow.Abort();
    }

    protected virtual bool AllowEscape => true;
}
