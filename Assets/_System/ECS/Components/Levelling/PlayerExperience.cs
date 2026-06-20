using Unity.Entities;
using Unity.Mathematics;

public struct PlayerExperience : IComponentData
{
    public int Level;
    public float Experience;
    public int NextLevelExperienceRequired;

    /// <summary>
    /// XP required to advance from <paramref name="currentLevel"/> to the next level.
    /// Front-loaded curve: round(110 * level^1.5) — fast early levels, slowing as the run progresses.
    /// Tuned so a baseline player reaches ~L18 by the boss (~4-5 spells with a spell every 4 levels).
    /// Single source of truth: used by PlayerProgressionSystem (live), PlayerAuthoring (initial), and
    /// the debug level-up command, so the curve can never drift between them.
    /// </summary>
    public static int XpForNextLevel(int currentLevel)
    {
        int l = currentLevel < 1 ? 1 : currentLevel;
        return (int)math.round(110f * math.pow((float)l, 1.5f));
    }
}
