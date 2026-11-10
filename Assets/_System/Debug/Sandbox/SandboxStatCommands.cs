#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using Unity.Entities;

/// <summary>Player stat commands.</summary>
public static class SandboxStatCommands
{
    [ConsoleCommand("set_stat", "Fixe une stat du joueur (valeur absolue ; entiers arrondis ; MaxHealth remet les PV au max).")]
    private static string SetStat(string stat, float value)
    {
        if (!TryResolveStat(stat, out var s, out var error))
            return error;
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;

        var core = em.GetComponentData<CoreStats>(player);
        if (!SandboxStatMap.TrySet(ref core, s, value))
            return $"{s} n'a pas de champ CoreStats : refusé.";
        em.SetComponentData(player, core);
        if (s == ECharacterStat.MaxHealth)
            em.SetComponentData(player, new Health { Value = (int)core.MaxHealth });
        em.AddComponent<SpellStatsCalculationRequest>(player);

        SandboxStatMap.TryGet(core, s, out var applied);
        SandboxSession.RecordStat(s, applied);
        return $"{s} = {applied}";
    }

    [ConsoleCommand("reset_stats", "Stats d'un spawn neuf : base du perso + amulette + méta, buffs vidés, PV au max (upgrades de spells inchangées).")]
    private static string ResetStats()
    {
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;

        using var selection = em.CreateEntityQuery(typeof(SelectedCharacter));
        using var database = em.CreateEntityQuery(typeof(CharactersDatabase), typeof(CharacterPrefabBufferElement));
        if (selection.IsEmpty || database.IsEmpty)
            return "Base de persos ou sélection introuvable.";
        int dbIndex = selection.GetSingleton<SelectedCharacter>().DbIndex;
        var prefabs = em.GetBuffer<CharacterPrefabBufferElement>(database.GetSingletonEntity(), true);
        if (dbIndex < 0 || dbIndex >= prefabs.Length || prefabs[dbIndex].CharacterPrefabEntity == Entity.Null)
            return $"Perso #{dbIndex} absent de la base.";
        var prefab = prefabs[dbIndex].CharacterPrefabEntity;

        var core = em.GetComponentData<CoreStats>(prefab);
        em.SetComponentData(player, core);
        if (em.HasComponent<DashEffect>(prefab) && em.HasComponent<DashEffect>(player))
            em.SetComponentData(player, em.GetComponentData<DashEffect>(prefab));
        if (em.HasBuffer<CharacterStatBuff>(player))
            em.GetBuffer<CharacterStatBuff>(player).Clear();

        // Amulet + meta-progression, exactly as PlayerSpawnerSystem does on spawn.
        using (var amulet = em.CreateEntityQuery(typeof(EquippedAmulet)))
            if (amulet.TryGetSingleton<EquippedAmulet>(out var equipped) && equipped.DbIndex > -1)
                AddOrSet(em, player, new ApplyAmuletRequest { DatabaseIndex = equipped.DbIndex });
        using (var gameState = em.CreateEntityQuery(typeof(GameState)))
        {
            var gs = gameState.GetSingletonEntity();
            if (em.HasBuffer<MetaProgressionLevelElement>(gs))
                AddOrSet(em, player, MetaBonusUtils.Build(em.GetBuffer<MetaProgressionLevelElement>(gs, true)));
        }

        em.SetComponentData(player, new Health { Value = (int)core.MaxHealth });
        em.AddComponent<SpellStatsCalculationRequest>(player);
        SandboxSession.ClearStats();
        SandboxRunner.Run(FillHealthAfterRequests());
        return "Stats remises à celles d'un spawn neuf (PV remis au max une fois amulette et méta appliquées).";
    }

    [ConsoleCommand("god", "Le joueur ne perd plus de PV, hazards et dash compris (on/off).")]
    private static string God(bool on)
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        var s = SandboxCommandUtils.GetSettings(em);
        s.PlayerGod = on;
        SandboxCommandUtils.SetSettings(em, s);
        return on ? "god : on" : "god : off";
    }

    /// <summary>Enum name (exact, then substring) or a defined numeric value; Enum.Parse would accept any number.</summary>
    internal static bool TryResolveStat(string query, out ECharacterStat stat, out string error)
    {
        stat = ECharacterStat.None;
        error = null;
        if (int.TryParse(query, out var raw))
        {
            if (raw == 0 || !Enum.IsDefined(typeof(ECharacterStat), raw))
            {
                error = $"Stat {raw} hors enum : refusé.";
                return false;
            }
            stat = (ECharacterStat)raw;
            return true;
        }
        var all = ((ECharacterStat[])Enum.GetValues(typeof(ECharacterStat))).Where(v => v != ECharacterStat.None);
        return SandboxCommandUtils.TryResolve(query, all, v => new[] { v.ToString() }, v => v.ToString(), out stat, out error);
    }

    private static void AddOrSet<T>(EntityManager em, Entity e, T data) where T : unmanaged, IComponentData
    {
        if (em.HasComponent<T>(e)) em.SetComponentData(e, data);
        else em.AddComponentData(e, data);
    }

    private static IEnumerator FillHealthAfterRequests()
    {
        for (int frame = 0; frame < 30; frame++)
        {
            yield return null;
            if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
                yield break;
            if (em.HasComponent<ApplyAmuletRequest>(player) || em.HasComponent<ApplyMetaProgressionRequest>(player))
                continue;
            em.SetComponentData(player, new Health { Value = (int)em.GetComponentData<CoreStats>(player).MaxHealth });
            yield break;
        }
    }
}
#endif
