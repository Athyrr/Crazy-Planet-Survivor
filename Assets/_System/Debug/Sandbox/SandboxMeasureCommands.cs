#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Entities;

/// <summary>Measure commands. World time; results go to the console and to SandboxSession for the window.</summary>
public static class SandboxMeasureCommands
{
    private const float TtkTimeoutSeconds = 120f;

    [ConsoleCommand("dps_reset", "Remet à zéro les compteurs des dummies et des spells.")]
    private static string DpsReset()
    {
        if (!SandboxCommandUtils.TryGetWorld(out var world, out var em))
            return SandboxCommandUtils.NoWorld;
        SandboxMetrics.ResetCounters(em);
        SandboxSession.MeasureResetTime = world.Time.ElapsedTime;
        return "Compteurs remis à zéro.";
    }

    [ConsoleCommand("measure", "Reset, attend <seconds> s (temps monde) et affiche les dégâts ; 'freeze' fige le joueur pendant la mesure.")]
    private static string Measure(float seconds, string mode = "")
    {
        if (seconds <= 0f)
            return "La durée doit être > 0.";
        bool freeze = string.Equals(mode, "freeze", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(mode) && !freeze)
            return "Usage : measure <s> [freeze]";
        if (!SandboxCommandUtils.TryGetPlayer(out _, out _))
            return SandboxCommandUtils.NoPlayer;
        SandboxRunner.Run(MeasureRoutine(seconds, freeze));
        return $"Mesure de {seconds} s lancée{(freeze ? " (joueur figé)" : "")}.";
    }

    [ConsoleCommand("ttk", "Sandbox : spawne <count> ennemis du type, affiche le temps pour tous les tuer et les PV perdus par le joueur.")]
    private static string Ttk(string type, int count)
    {
        if (count < 1)
            return "n doit être >= 1.";
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;
        if (!SandboxCommandUtils.IsSandboxPlanet(em))
            return SandboxCommandUtils.SandboxOnly;
        if (!SandboxCommandUtils.TryResolveEnemyPrefab(em, type, out var prefab, out var name, out var error))
            return error;

        // Player damage taken during the ttk (after armor): the player's DamageTally, reset here.
        if (em.HasComponent<DamageTally>(player)) em.SetComponentData(player, default(DamageTally));
        else em.AddComponent<DamageTally>(player);

        var spawned = SandboxSpawnUtils.SpawnEnemies(em, player, prefab, count, Allocator.Temp);
        var targets = spawned.ToArray();
        spawned.Dispose();
        SandboxCommandUtils.TryGetWorld(out var world, out _);
        SandboxRunner.Run(TtkRoutine(name, targets, world.Time.ElapsedTime));
        return $"ttk : {count} × {name} spawnés (PV de base), chrono lancé.";
    }

    private static IEnumerator MeasureRoutine(float seconds, bool freeze)
    {
        if (!SandboxCommandUtils.TryGetWorld(out var world, out var em))
            yield break;
        if (freeze)
            SetPlayerFrozen(em, true);

        SandboxMetrics.ResetCounters(em);
        double t0 = world.Time.ElapsedTime;
        SandboxSession.MeasureResetTime = t0;

        while (true)
        {
            yield return null;
            if (!SandboxCommandUtils.TryGetWorld(out world, out em))
                yield break;
            if (world.Time.ElapsedTime - t0 >= seconds)
                break;
        }

        var snap = SandboxMetrics.Capture();
        if (freeze)
            SetPlayerFrozen(em, false);

        double dt = Math.Max(1e-6, snap.Time - t0);
        var sb = new StringBuilder($"measure {seconds:0.##} s{(freeze ? " (figé)" : "")} :");
        float dummyTotal = 0f;
        foreach (var d in snap.Dummies)
        {
            dummyTotal += d.Total;
            sb.Append($"\n  dummy {d.Entity.Index} : {d.Total:0} ({d.Total / dt:0.0}/s), plus gros coup {d.MaxHit:0}, armure {d.Armor:0.#}");
        }
        sb.Append($"\n  dummies : {dummyTotal:0} ({dummyTotal / dt:0.0}/s) — après armure");
        foreach (var s in snap.Spells)
            sb.Append($"\n  {s.Id} : {s.TotalDamage:0} ({s.TotalDamage / dt:0.0}/s) — avant armure");

        SandboxSession.LastMeasureResult = sb.ToString();
        SandboxRunner.Report(SandboxSession.LastMeasureResult);
    }

    private static IEnumerator TtkRoutine(string name, Entity[] targets, double t0)
    {
        while (true)
        {
            yield return null;
            if (!SandboxCommandUtils.TryGetWorld(out var world, out var em))
                yield break;

            int alive = 0;
            foreach (var e in targets)
                if (em.Exists(e) && !(em.HasComponent<DestroyEntityFlag>(e) && em.IsComponentEnabled<DestroyEntityFlag>(e)))
                    alive++;

            double elapsed = world.Time.ElapsedTime - t0;
            if (alive > 0 && elapsed < TtkTimeoutSeconds)
                continue;

            float lost = 0f;
            if (SandboxCommandUtils.TryGetPlayer(out _, out var player) && em.HasComponent<DamageTally>(player))
                lost = em.GetComponentData<DamageTally>(player).Total;

            SandboxSession.LastTtkResult = alive == 0
                ? $"ttk {name} ×{targets.Length} : {elapsed:0.00} s, PV perdus par le joueur : {lost:0}"
                : $"ttk {name} ×{targets.Length} : abandon après {TtkTimeoutSeconds:0} s ({alive} encore en vie), PV perdus : {lost:0}";
            SandboxRunner.Report(SandboxSession.LastTtkResult);
            yield break;
        }
    }

    private static void SetPlayerFrozen(EntityManager em, bool frozen)
    {
        var s = SandboxCommandUtils.GetSettings(em);
        s.PlayerFrozen = frozen;
        SandboxCommandUtils.SetSettings(em, s);
    }
}
#endif
