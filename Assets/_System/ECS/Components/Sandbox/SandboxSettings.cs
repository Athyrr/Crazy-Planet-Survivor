using Unity.Entities;

/// <summary>Runtime state of the sandbox tools. One singleton carrying RunScope, so RunCleanerSystem
/// destroys it at run end and everything resets on exit. Created on the Sandbox planet by SandboxModeApplySystem,
/// elsewhere by the first command that needs it. Always compiled (read by guards in shipped systems), never baked.</summary>
public struct SandboxSettings : IComponentData
{
    /// <summary>True when created by the Sandbox bootstrap (false = created by a command on another planet).</summary>
    public bool IsSandboxPlanet;
    /// <summary>EnemiesSpawnerSystem: kills are still credited, wave timers / director / spawning are frozen.</summary>
    public bool SpawnPaused;
    /// <summary>Player gets a HealthFloor: a lethal blow brings it back to MaxHealth instead of a GameOver.</summary>
    public bool PlayerHealthFloor;
    /// <summary>Forced difficulty units (EnemyScalingConfig "D"); &lt; 0 = off, normal time + kills scaling.</summary>
    public float DifficultyLevel;
    public bool EnemiesFrozen;
    public bool EnemiesInvincible;
    public bool EnemiesImmortal;
    /// <summary>Seconds without a damaging hit before an immortal enemy heals back to full.</summary>
    public float ImmortalRegenDelay;
    public bool PlayerGod;
    /// <summary>`measure &lt;s&gt; freeze`: the player gets no input (no move, no dash); its spells keep firing.</summary>
    public bool PlayerFrozen;
    /// <summary>Lethal blows caught by the player's floor (drained from HealthFloor.FloorHits).</summary>
    public int PlayerDeaths;

    public const float DefaultRegenDelay = 3f;

    /// <summary>Created by a command outside the Sandbox: every mode off, normal difficulty.</summary>
    public static SandboxSettings Off => new SandboxSettings
    {
        DifficultyLevel = -1f,
        ImmortalRegenDelay = DefaultRegenDelay,
    };

    /// <summary>Sandbox planet start: spawn paused, player floor on, difficulty frozen at level 0, enemy modes off.</summary>
    public static SandboxSettings SandboxPlanet => new SandboxSettings
    {
        IsSandboxPlanet = true,
        SpawnPaused = true,
        PlayerHealthFloor = true,
        DifficultyLevel = 0f,
        ImmortalRegenDelay = DefaultRegenDelay,
    };
}

/// <summary>Forced difficulty for the three readers of EnemyScalingConfig (HealthSystem, SpellCastingSystem,
/// EnemiesSpawnerSystem). Replaces only the inputs (timer, kills): the global EnemyScalingConfig is never written.</summary>
public static class SandboxDifficulty
{
    public static void Apply(in SandboxSettings settings, in EnemyScalingConfig cfg, ref float timer, ref float kills)
    {
        if (settings.DifficultyLevel < 0f)
            return;

        float secondsPerUnit = cfg.SecondsPerUnit > 0f ? cfg.SecondsPerUnit : 60f;
        timer = settings.DifficultyLevel * secondsPerUnit;
        kills = 0f;
    }
}
