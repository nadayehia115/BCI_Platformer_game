using System;
using UnityEngine;

// Holds the player's score. The score UI can subscribe to Changed (or read Score) later.
public static class ScoreManager
{
    public static int Score { get; private set; }

    public static event Action<int> Changed;

    public static void Add(int points)
    {
        Score += points;
        Changed?.Invoke(Score);
    }

    public static void ResetScore()
    {
        Score = 0;
        Changed?.Invoke(Score);
    }

    // Static fields survive Play sessions when domain reload is disabled, so start every run from zero.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Score = 0;
        Changed = null;
    }
}
