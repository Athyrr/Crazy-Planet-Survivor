#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;

/// <summary>Spawn / enemy / dummy commands. Modes apply to present and future enemies through
/// SandboxModeApplySystem; spawn_enemy, spawn_dummy, dummy_hp, dummy_armor are Sandbox-only.</summary>
public static class SandboxEnemyCommands
{
    [ConsoleCommand("spawn", "Vagues on / off.")]
    private static string Spawn(bool on) => SetFlag((ref SandboxSettings s) => s.SpawnPaused = !on, on ? "spawn : on" : "spawn : off");

    [ConsoleCommand("enemies_freeze", "Ennemis présents et futurs immobiles (ils castent encore).")]
    private static string EnemiesFreeze(bool on) => SetFlag((ref SandboxSettings s) => s.EnemiesFrozen = on, $"enemies_freeze : {(on ? "on" : "off")}");

    [ConsoleCommand("enemies_invincible", "Ennemis présents et futurs : coups reçus à 0 dégât.")]
    private static string EnemiesInvincible(bool on) => SetFlag((ref SandboxSettings s) => s.EnemiesInvincible = on, $"enemies_invincible : {(on ? "on" : "off")}");

    [ConsoleCommand("enemies_immortal", "Ennemis présents et futurs : plancher 1 PV, soin complet après <delay> s sans coup.")]
    private static string EnemiesImmortal(bool on, float delay = SandboxSettings.DefaultRegenDelay)
    {
        if (delay < 0f)
            return "Le délai doit être >= 0.";
        return SetFlag((ref SandboxSettings s) => { s.EnemiesImmortal = on; s.ImmortalRegenDelay = delay; },
            on ? $"enemies_immortal : on (soin après {delay} s)" : "enemies_immortal : off");
    }

    [ConsoleCommand("difficulty", "Force le scaling ennemi au palier n (unités de difficulté) ; off = normal.")]
    private static string Difficulty(string level)
    {
        float value;
        if (string.Equals(level, "off", StringComparison.OrdinalIgnoreCase))
            value = -1f;
        else if (!float.TryParse(level, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0f)
            return "Usage : difficulty <n >= 0> | off";

        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        var s = SandboxCommandUtils.GetSettings(em);
        s.DifficultyLevel = value;
        SandboxCommandUtils.SetSettings(em, s);
        if (value < 0f)
            return "difficulty : off (scaling normal)";

        using var cfgQuery = em.CreateEntityQuery(typeof(EnemyScalingConfig));
        var cfg = cfgQuery.TryGetSingleton<EnemyScalingConfig>(out var c) ? c : EnemyScalingConfig.Default;
        float timer = 0f, kills = 0f;
        SandboxDifficulty.Apply(in s, in cfg, ref timer, ref kills);
        return $"difficulty : palier {value} (PV ×{cfg.ComputeHealthMult(timer, kills):0.##}, dégâts ×{cfg.ComputeDamageMult(timer, kills):0.##} sur les vagues ; spawn_enemy reste en PV de base)";
    }

    [ConsoleCommand("max_enemies", "Plafond d'ennemis du spawner de la planète.")]
    private static string MaxEnemies(int count)
    {
        if (count < 0)
            return "n doit être >= 0.";
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        using var q = em.CreateEntityQuery(typeof(SpawnerSettings));
        if (!q.TryGetSingleton<SpawnerSettings>(out var settings))
            return "Pas de spawner sur cette planète.";
        settings.MaxEnemies = count;
        q.SetSingleton(settings);
        return $"max_enemies = {count}";
    }

    [ConsoleCommand("director", "Assault director on / off.")]
    private static string Director(bool on)
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        using var q = em.CreateEntityQuery(typeof(SpawnIntensityConfig));
        if (!q.TryGetSingleton<SpawnIntensityConfig>(out var cfg))
            return "Pas de director sur cette planète.";
        cfg.Enabled = on;
        q.SetSingleton(cfg);
        return $"director : {(on ? "on" : "off")}";
    }

    [ConsoleCommand("kill_all", "Tue tous les ennemis (comptés comme tués, le spawner n'est pas bloqué) ; dummies exclus.")]
    private static string KillAll()
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        using var q = em.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Enemy>() },
            None = new[] { ComponentType.ReadOnly<DummyTag>() },
        });
        q.CompleteDependency();
        var entities = q.ToEntityArray(Allocator.Temp);
        var enemies = q.ToComponentDataArray<Enemy>(Allocator.Temp);
        int killed = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            if (!em.HasComponent<DestroyEntityFlag>(e) || em.IsComponentEnabled<DestroyEntityFlag>(e))
                continue;
            em.SetComponentEnabled<DestroyEntityFlag>(e, true);
            var evt = em.CreateEntity(typeof(EnemyKilledEvent));
            em.SetComponentData(evt, new EnemyKilledEvent { WaveIndex = enemies[i].WaveIndex });
            killed++;
        }
        enemies.Dispose();
        entities.Dispose();
        return $"{killed} ennemi(s) tué(s) (dummies épargnés).";
    }

    [ConsoleCommand("spawn_enemy", "Sandbox : n ennemis du type autour du joueur, PV de base (sans scaling).")]
    private static string SpawnEnemy(string type, int count = 1)
    {
        if (count < 1)
            return "n doit être >= 1.";
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;
        if (!SandboxCommandUtils.IsSandboxPlanet(em))
            return SandboxCommandUtils.SandboxOnly;
        if (!SandboxCommandUtils.TryResolveEnemyPrefab(em, type, out var prefab, out var name, out var error))
            return error;
        SandboxSpawnUtils.SpawnEnemies(em, player, prefab, count, Allocator.Temp).Dispose();
        return $"{count} × {name} (PV de base).";
    }

    [ConsoleCommand("spawn_dummy", "Sandbox : n dummies autour du joueur.")]
    private static string SpawnDummy(int count = 1)
    {
        if (count < 1)
            return "n doit être >= 1.";
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;
        if (!SandboxCommandUtils.IsSandboxPlanet(em) || !SandboxCommandUtils.TryGetRegistry(em, out var reg))
            return SandboxCommandUtils.SandboxOnly;
        var prefab = em.GetComponentData<SandboxRegistry>(reg).DummyPrefab;
        if (prefab == Entity.Null)
            return "PF_Dummy absent du registre Sandbox.";
        // Dummies are never killed: not counted in ActiveEnemyCount. Root at R + 1 (capsule base on the ground).
        SandboxSpawnUtils.SpawnAroundPlayer(em, player, prefab, count, 1f, Allocator.Temp).Dispose();
        return $"{count} dummy(s).";
    }

    [ConsoleCommand("dummy_hp", "Sandbox : PV max (et PV) de tous les dummies.")]
    private static string DummyHp(int hp)
    {
        if (hp < 1)
            return "PV >= 1.";
        return ForEachDummy((ref CoreStats core, ref Health health) => { core.MaxHealth = hp; health.Value = hp; }, $"dummies : {hp} PV");
    }

    [ConsoleCommand("dummy_armor", "Sandbox : armure de tous les dummies.")]
    private static string DummyArmor(float armor)
    {
        if (armor < 0f)
            return "Armure >= 0.";
        return ForEachDummy((ref CoreStats core, ref Health health) => core.Armor = armor, $"dummies : armure {armor}");
    }

    private delegate void DummyEdit(ref CoreStats core, ref Health health);

    private static string ForEachDummy(DummyEdit edit, string done)
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        if (!SandboxCommandUtils.IsSandboxPlanet(em))
            return SandboxCommandUtils.SandboxOnly;
        using var q = em.CreateEntityQuery(typeof(DummyTag), typeof(CoreStats), typeof(Health));
        q.CompleteDependency();
        var dummies = q.ToEntityArray(Allocator.Temp);
        foreach (var d in dummies)
        {
            var core = em.GetComponentData<CoreStats>(d);
            var health = em.GetComponentData<Health>(d);
            edit(ref core, ref health);
            em.SetComponentData(d, core);
            em.SetComponentData(d, health);
        }
        int n = dummies.Length;
        dummies.Dispose();
        return $"{done} ({n} dummy(s)).";
    }

    private delegate void SettingsEdit(ref SandboxSettings s);

    private static string SetFlag(SettingsEdit edit, string done)
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        var s = SandboxCommandUtils.GetSettings(em);
        edit(ref s);
        SandboxCommandUtils.SetSettings(em, s);
        return done;
    }
}
#endif
