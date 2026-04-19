using System.Linq;
using System.Text;
using Unity.Entities;
using UnityEngine;

public static class DefaultConsoleCommands
{
    [ConsoleCommand("help", "List every registered command (or describe one).")]
    private static string Help(string command = "")
    {
        if (!string.IsNullOrEmpty(command))
        {
            if (!ConsoleCommandRegistry.TryGet(command, out var entry))
                return $"Unknown command '{command}'.";
            return $"{entry.Usage}\n  {entry.Description}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Available commands:");
        foreach (var e in ConsoleCommandRegistry.All)
            sb.AppendLine($"  <color=#9cdcfe>{e.Name}</color> — {e.Description}");
        return sb.ToString().TrimEnd();
    }

    [ConsoleCommand("clear", "Clear the console output.")]
    private static void Clear() => DeveloperConsole.Clear();

    [ConsoleCommand("quit", "Quit the application.")]
    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    [ConsoleCommand("timescale", "Get or set Time.timeScale.")]
    private static string TimeScale(float value = -1f)
    {
        if (value >= 0f) Time.timeScale = value;
        return $"Time.timeScale = {Time.timeScale}";
    }

    // ---------------- Gameplay test commands ----------------

    [ConsoleCommand("level_up", "Level the player up N times (default 1).")]
    private static string LevelUp(int count = 1)
    {
        if (count < 1) return "Count must be >= 1.";
        if (!TryGetPlayer(out var world, out var entity, out var experience))
            return "No Player entity found. Are you in a run?";

        var em = world.EntityManager;
        for (int i = 0; i < count; i++)
        {
            // Mirror PlayerProgressionSystem.GainExperienceJob's level-up math.
            experience.Experience = 0f;
            experience.Level++;
            float nextLevelExperience = experience.Level * 500 + (experience.NextLevelExperienceRequired * 0.5f) + 1000;
            experience.NextLevelExperienceRequired = (int)nextLevelExperience;
        }
        em.SetComponentData(entity, experience);

        if (!em.HasComponent<PlayerLevelUpRequest>(entity))
            em.AddComponentData(entity, new PlayerLevelUpRequest());

        return $"Player is now level {experience.Level} (+{count}).";
    }

    [ConsoleCommand("give_xp", "Add raw experience to the player.")]
    private static string GiveXp(float amount)
    {
        if (!TryGetPlayer(out var world, out var entity, out var experience))
            return "No Player entity found. Are you in a run?";
        experience.Experience += amount;
        world.EntityManager.SetComponentData(entity, experience);
        return $"Added {amount} XP. ({experience.Experience}/{experience.NextLevelExperienceRequired})";
    }

    [ConsoleCommand("get_level", "Print the player's current level and XP.")]
    private static string GetLevel()
    {
        if (!TryGetPlayer(out _, out _, out var experience))
            return "No Player entity found.";
        return $"Level {experience.Level} — {experience.Experience}/{experience.NextLevelExperienceRequired} XP";
    }

    private static bool TryGetPlayer(out World world, out Entity entity, out PlayerExperience experience)
    {
        world = World.DefaultGameObjectInjectionWorld;
        entity = Entity.Null;
        experience = default;
        if (world == null || !world.IsCreated) return false;

        var em = world.EntityManager;
        using var query = em.CreateEntityQuery(typeof(Player), typeof(PlayerExperience));
        if (query.IsEmpty) return false;

        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        if (entities.Length == 0) { entities.Dispose(); return false; }

        entity = entities[0];
        entities.Dispose();
        experience = em.GetComponentData<PlayerExperience>(entity);
        return true;
    }
}
