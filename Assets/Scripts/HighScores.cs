using UnityEngine;

// The top 3 scores, kept on the player's computer (PlayerPrefs), so they are still there next time the game is started.
// LevelEnd adds a score when the player wins; the main menu shows the list (HighScoreBoard).
public static class HighScores
{
    public const int Count = 3;
    const string Key = "HighScore";                  // HighScore0, HighScore1, HighScore2

    // The best scores, best first. 0 = nobody has taken that place yet.
    public static int[] Top()
    {
        var scores = new int[Count];
        for (int i = 0; i < Count; i++) scores[i] = PlayerPrefs.GetInt(Key + i, 0);
        return scores;
    }

    // Adds a score if it makes the top 3. Returns the place it took (0 = first), or -1 if it did not make it (or was 0).
    public static int Submit(int score)
    {
        if (score <= 0) return -1;
        var scores = Top();
        int place = -1;
        for (int i = 0; i < Count; i++)
        {
            if (score > scores[i]) { place = i; break; }          // a tie stays below the older score
        }
        if (place < 0) return -1;

        for (int i = Count - 1; i > place; i--) scores[i] = scores[i - 1];    // everyone below moves down one place
        scores[place] = score;
        for (int i = 0; i < Count; i++) PlayerPrefs.SetInt(Key + i, scores[i]);
        PlayerPrefs.Save();
        return place;
    }

    // Forgets all scores (for testing: or call it from a menu button later).
    public static void Clear()
    {
        for (int i = 0; i < Count; i++) PlayerPrefs.DeleteKey(Key + i);
        PlayerPrefs.Save();
    }
}
