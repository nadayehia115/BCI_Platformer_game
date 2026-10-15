using UnityEngine;

// Controls that arrive from outside the keyboard (the Python script, later the BCI model), received by BciListener.
// The player reads this next to the keyboard, so both work at the same time.
//
// Holding a key is "pressed down until released"; over UDP the equivalent is the sender repeating "move|1" about 20 times a
// second. Each message renews the command, and if the messages stop for TimeoutSeconds the movement stops by itself, so a
// crashed or frozen sender can never leave the player running.
public static class RemoteInput
{
    const float TimeoutSeconds = 0.3f;     // movement is dropped this long after the last "move" message
    const float JumpBufferSeconds = 0.2f;  // a jump request waits this long for the player to touch the ground

    static float horizontal;
    static float horizontalAt = float.NegativeInfinity;
    static float jumpAt = float.NegativeInfinity;

    // -1 = left, 0 = still, 1 = right (like the keyboard's Horizontal axis)
    public static float Horizontal => Time.unscaledTime - horizontalAt <= TimeoutSeconds ? horizontal : 0f;

    public static void SetHorizontal(float value)
    {
        horizontal = Mathf.Clamp(value, -1f, 1f);
        horizontalAt = Time.unscaledTime;
    }

    public static void RequestJump() => jumpAt = Time.unscaledTime;

    // true once per request; the player calls it only while standing on the ground
    public static bool ConsumeJump()
    {
        if (Time.unscaledTime - jumpAt > JumpBufferSeconds) return false;
        jumpAt = float.NegativeInfinity;
        return true;
    }

    // Static fields survive Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        horizontal = 0f;
        horizontalAt = float.NegativeInfinity;
        jumpAt = float.NegativeInfinity;
    }
}
