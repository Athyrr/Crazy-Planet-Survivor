#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Entities;
using UnityEngine;

/// <summary>Named sandbox setup (scene-de-test). Names only (asset names, ESpellID names), never a database index:
/// DB indices are not stable across bakes (DB sorted by asset name).</summary>
[Serializable]
public sealed class SandboxPreset
{
    [Serializable]
    public struct StatEntry
    {
        public string Stat;
        public float Value;
    }

    [Serializable]
    public struct SpellEntry
    {
        public string Spell;
        public int Explode; // -1 unchanged, 0 off, 1 on
        public int Emit;
        public int Bounce;  // -1 unchanged
        public int Pierce;
    }

    public string Character = string.Empty;
    public List<string> Spells = new List<string>();
    public List<string> Upgrades = new List<string>();
    public List<StatEntry> Stats = new List<StatEntry>();
    public List<SpellEntry> SpellOverrides = new List<SpellEntry>();
    public bool EnemiesFrozen;
    public bool EnemiesInvincible;
    public bool EnemiesImmortal;
    public float ImmortalRegenDelay = SandboxSettings.DefaultRegenDelay;
    public float Difficulty = -1f;
}

public static class SandboxPresets
{
    private const int MaxWaitFrames = 60;

    /// <summary>Editor: &lt;project&gt;/UserSettings/SandboxPresets (git-ignored). Development build: persistentDataPath/SandboxPresets.</summary>
    public static string Folder =>
#if UNITY_EDITOR
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "SandboxPresets"));
#else
        Path.Combine(Application.persistentDataPath, "SandboxPresets");
#endif

    public static string[] List() =>
        Directory.Exists(Folder)
            ? Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray()
            : Array.Empty<string>();

    private static string PathOf(string name)
    {
        var safe = new string(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return Path.Combine(Folder, safe + ".json");
    }

    public static bool TrySaveCurrent(string name, out string message)
    {
        if (string.IsNullOrWhiteSpace(name)) { message = "Nom de preset vide."; return false; }
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player)) { message = SandboxCommandUtils.NoPlayer; return false; }

        var preset = new SandboxPreset();
        using (var sel = em.CreateEntityQuery(typeof(SelectedCharacter)))
            if (sel.TryGetSingleton<SelectedCharacter>(out var selected))
                preset.Character = SandboxCommandUtils.CharacterKey(selected.DbIndex);
        foreach (var s in em.GetBuffer<ActiveSpell>(player, true))
            if (SandboxCommandUtils.TryGetSpellId(em, s.DatabaseIndex, out var id))
                preset.Spells.Add(id.ToString());
        preset.Upgrades.AddRange(SandboxSession.Upgrades);
        foreach (var kv in SandboxSession.FixedStats)
            preset.Stats.Add(new SandboxPreset.StatEntry { Stat = kv.Key.ToString(), Value = kv.Value });
        foreach (var o in SandboxSession.SpellOverrides)
            preset.SpellOverrides.Add(new SandboxPreset.SpellEntry { Spell = o.Spell.ToString(), Explode = o.Explode, Emit = o.Emit, Bounce = o.Bounce, Pierce = o.Pierce });
        var settings = SandboxCommandUtils.GetSettings(em);
        preset.EnemiesFrozen = settings.EnemiesFrozen;
        preset.EnemiesInvincible = settings.EnemiesInvincible;
        preset.EnemiesImmortal = settings.EnemiesImmortal;
        preset.ImmortalRegenDelay = settings.ImmortalRegenDelay;
        preset.Difficulty = settings.DifficultyLevel;

        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOf(name), JsonUtility.ToJson(preset, true));
        message = $"Preset '{name}' sauvé ({preset.Spells.Count} spell(s), {preset.Upgrades.Count} upgrade(s), {preset.Stats.Count} stat(s)).";
        return true;
    }

    public static bool TryLoad(string name, out SandboxPreset preset, out string error)
    {
        preset = null;
        error = null;
        var path = PathOf(name);
        if (!File.Exists(path)) { error = $"Preset '{name}' introuvable ({Folder})."; return false; }
        try
        {
            preset = JsonUtility.FromJson<SandboxPreset>(File.ReadAllText(path));
            if (preset == null) { error = $"Preset '{name}' illisible."; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = $"Preset '{name}' illisible : {ex.Message}";
            return false;
        }
    }

    public static bool Delete(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Runner routine. Waits for Running + one player (+ the Sandbox bootstrap on the Sandbox planet), then:
    /// spells → upgrades → fixed stats → capabilities / counters → enemy modes, through the commands themselves.</summary>
    public static IEnumerator Apply(SandboxPreset preset)
    {
        for (int frame = 0; ; frame++)
        {
            if (SandboxCommandUtils.TryGetPlayer(out var em, out _)
                && SandboxCommandUtils.GetGameState(em) == EGameState.Running
                && (!SandboxCommandUtils.IsSandboxPlanet(em) || SandboxCommandUtils.GetSettings(em).IsSandboxPlanet))
                break;
            if (frame > 600) { SandboxRunner.Report("[Sandbox] preset : joueur jamais prêt, abandon."); yield break; }
            yield return null;
        }

        // 1) Spells: exactly the preset's list.
        Exec("clear_spells");
        yield return null;
        foreach (var spell in preset.Spells)
            Exec($"add_spell {spell}");
        for (int i = 0; i < MaxWaitFrames && !AllSpellsActive(preset.Spells); i++)
            yield return null;

        // 2) Upgrades, in order, one per consumed ApplyUpgradeRequest.
        foreach (var upgrade in preset.Upgrades)
        {
            for (int i = 0; i < MaxWaitFrames && PlayerHas<ApplyUpgradeRequest>(); i++)
                yield return null;
            Exec($"give_upgrade \"{upgrade}\"");
            yield return null;
        }
        for (int i = 0; i < MaxWaitFrames && PlayerHas<ApplyUpgradeRequest>(); i++)
            yield return null;

        // 3) Fixed stats (after upgrades, which would add on top).
        foreach (var stat in preset.Stats)
            Exec($"set_stat {stat.Stat} {stat.Value.ToString(CultureInfo.InvariantCulture)}");
        yield return null;

        // 4) Capabilities and counters (counters need an up-to-date FinalBounces / FinalPierces).
        foreach (var o in preset.SpellOverrides)
        {
            if (o.Explode >= 0) Exec($"spell_cap {o.Spell} Explode {(o.Explode == 1 ? "on" : "off")}");
            if (o.Emit >= 0) Exec($"spell_cap {o.Spell} Emit {(o.Emit == 1 ? "on" : "off")}");
            if (o.Bounce >= 0)
            {
                for (int i = 0; i < MaxWaitFrames && PlayerHas<SpellStatsCalculationRequest>(); i++)
                    yield return null;
                Exec($"spell_bounce {o.Spell} {o.Bounce}");
            }
            if (o.Pierce >= 0)
            {
                for (int i = 0; i < MaxWaitFrames && PlayerHas<SpellStatsCalculationRequest>(); i++)
                    yield return null;
                Exec($"spell_pierce {o.Spell} {o.Pierce}");
            }
        }
        yield return null;

        // 5) Enemy modes and difficulty.
        Exec($"enemies_freeze {(preset.EnemiesFrozen ? "on" : "off")}");
        Exec($"enemies_invincible {(preset.EnemiesInvincible ? "on" : "off")}");
        Exec($"enemies_immortal {(preset.EnemiesImmortal ? "on" : "off")} {preset.ImmortalRegenDelay.ToString(CultureInfo.InvariantCulture)}");
        Exec(preset.Difficulty < 0f ? "difficulty off" : $"difficulty {preset.Difficulty.ToString(CultureInfo.InvariantCulture)}");

        SandboxRunner.Report("[Sandbox] preset appliqué.");
    }

    private static void Exec(string line)
    {
        var result = ConsoleCommandInvoker.Execute(line);
        SandboxRunner.Report($"[Sandbox] preset > {line} : {result.Output}");
    }

    private static bool PlayerHas<T>() where T : unmanaged, IComponentData =>
        SandboxCommandUtils.TryGetPlayer(out var em, out var player) && em.HasComponent<T>(player);

    private static bool AllSpellsActive(List<string> spells)
    {
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return false;
        var active = new HashSet<string>();
        foreach (var s in em.GetBuffer<ActiveSpell>(player, true))
            if (SandboxCommandUtils.TryGetSpellId(em, s.DatabaseIndex, out var id))
                active.Add(id.ToString());
        // Unknown names were already reported by add_spell: only wait for resolvable ones.
        return spells.All(n => !SandboxCommandUtils.TryResolveSpellId(n, out var id, out _) || active.Contains(id.ToString()));
    }
}
#endif
