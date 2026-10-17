using UnityEngine;

// En yüksek skor. Oyun bitince Submit(skor) çağrılır, ana menü Best'i gösterir.
public static class HighScore
{
    const string Key = "BigEs.HighScore";

    public static int Best => PlayerPrefs.GetInt(Key, 0);

    // Skor rekorsa kaydeder ve true döner.
    public static bool Submit(int score)
    {
        if (score <= Best)
            return false;
        PlayerPrefs.SetInt(Key, score);
        PlayerPrefs.Save();
        return true;
    }
}
