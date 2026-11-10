#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>Shared helpers of the sandbox commands: world / player / settings access, name resolution
/// (exact case-insensitive first, then substring — like give_upgrade), spell instance cleanup.</summary>
public static class SandboxCommandUtils
{
    public const string SandboxOnly = "Sandbox uniquement (lance `sandbox` d'abord).";
    public const string NoPlayer = "Aucun joueur : es-tu dans une run ?";
    public const string NoWorld = "Pas de monde ECS.";

    public static bool TryGetWorld(out World world, out EntityManager em)
    {
        world = World.DefaultGameObjectInjectionWorld;
        em = default;
        if (world == null || !world.IsCreated)
            return false;
        em = world.EntityManager;
        return true;
    }

    /// <summary>Exactly one Player entity (two players would break every GetSingletonEntity&lt;Player&gt;).</summary>
    public static bool TryGetPlayer(out EntityManager em, out Entity player)
    {
        player = Entity.Null;
        if (!TryGetWorld(out _, out em))
            return false;
        using var q = em.CreateEntityQuery(typeof(Player));
        if (q.CalculateEntityCount() != 1)
            return false;
        player = q.GetSingletonEntity();
        return true;
    }

    public static EGameState GetGameState(EntityManager em)
    {
        using var q = em.CreateEntityQuery(typeof(GameState));
        return q.TryGetSingleton<GameState>(out var gs) ? gs.State : EGameState.MainMenu;
    }

    public static bool IsSandboxPlanet(EntityManager em)
    {
        using var q = em.CreateEntityQuery(typeof(PlanetData));
        return q.TryGetSingleton<PlanetData>(out var p) && p.PlanetID == EPlanetID.Sandbox;
    }

    /// <summary>The SandboxSettings singleton; created (all modes off, RunScope) when missing.</summary>
    public static Entity EnsureSandboxSettings(EntityManager em)
    {
        using var q = em.CreateEntityQuery(typeof(SandboxSettings));
        if (!q.IsEmpty)
            return q.GetSingletonEntity();
        var e = em.CreateEntity(typeof(SandboxSettings), typeof(RunScope));
        em.SetComponentData(e, SandboxSettings.Off);
        return e;
    }

    public static SandboxSettings GetSettings(EntityManager em) => em.GetComponentData<SandboxSettings>(EnsureSandboxSettings(em));

    public static void SetSettings(EntityManager em, SandboxSettings s) => em.SetComponentData(EnsureSandboxSettings(em), s);

    public static bool TryResolve<T>(string query, IEnumerable<T> items, Func<T, IEnumerable<string>> names,
        Func<T, string> label, out T match, out string error)
    {
        match = default;
        error = null;
        if (string.IsNullOrWhiteSpace(query))
        {
            error = "Nom vide.";
            return false;
        }

        var all = items.ToList();
        var found = all.Where(i => names(i).Any(n => string.Equals(n, query, StringComparison.OrdinalIgnoreCase))).ToList();
        if (found.Count == 0)
            found = all.Where(i => names(i).Any(n => !string.IsNullOrEmpty(n)
                                                     && n.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        if (found.Count == 1)
        {
            match = found[0];
            return true;
        }

        error = found.Count == 0
            ? $"Aucun résultat pour '{query}'."
            : $"'{query}' est ambigu : {string.Join(", ", found.Select(label))}";
        return false;
    }

    // ---------------- Spells ----------------

    public static bool TryGetSpellsDb(EntityManager em, out BlobAssetReference<SpellBlobs> db)
    {
        db = default;
        using var q = em.CreateEntityQuery(typeof(SpellsDatabase));
        if (!q.TryGetSingleton<SpellsDatabase>(out var sdb) || !sdb.Blobs.IsCreated)
            return false;
        db = sdb.Blobs;
        return true;
    }

    /// <summary>A numeric ESpellID value, or an ESpellID name (exact, then substring).</summary>
    public static bool TryResolveSpellId(string query, out ESpellID id, out string error)
    {
        id = ESpellID.None;
        error = null;
        if (byte.TryParse(query, out var raw))
        {
            if (raw != 0 && Enum.IsDefined(typeof(ESpellID), raw))
            {
                id = (ESpellID)raw;
                return true;
            }
            error = $"ESpellID {raw} inconnu.";
            return false;
        }

        var values = ((ESpellID[])Enum.GetValues(typeof(ESpellID))).Where(v => v != ESpellID.None);
        return TryResolve(query, values, v => new[] { v.ToString() }, v => v.ToString(), out id, out error);
    }

    /// <summary>First DB index with that ID (SpellActivationSystem builds its map with TryAdd: first wins).</summary>
    public static int FindSpellDbIndex(EntityManager em, ESpellID id)
    {
        if (!TryGetSpellsDb(em, out var db))
            return -1;
        ref var spells = ref db.Value.Spells;
        for (int i = 0; i < spells.Length; i++)
            if (spells[i].ID == id)
                return i;
        return -1;
    }

    public static bool TryGetSpellId(EntityManager em, int dbIndex, out ESpellID id)
    {
        id = ESpellID.None;
        if (!TryGetSpellsDb(em, out var db))
            return false;
        ref var spells = ref db.Value.Spells;
        if (dbIndex < 0 || dbIndex >= spells.Length)
            return false;
        id = spells[dbIndex].ID;
        return true;
    }

    public static int FindActiveSpell(DynamicBuffer<ActiveSpell> spells, int dbIndex)
    {
        for (int i = 0; i < spells.Length; i++)
            if (spells[i].DatabaseIndex == dbIndex)
                return i;
        return -1;
    }

    /// <summary>Destroys every live instance cast by caster (projectiles, OverTime zones, FireOrbs / beam owners with
    /// their LinkedEntityGroup) for that root spell, or all of them when dbIndex &lt; 0. Array overload: an owner's
    /// LinkedEntityGroup may already list some of the same entities (same reason as RunCleanerSystem).</summary>
    public static int DestroySpellInstances(EntityManager em, Entity caster, int dbIndex)
    {
        using var q = em.CreateEntityQuery(ComponentType.ReadOnly<SpellSource>());
        q.CompleteDependency();
        var entities = q.ToEntityArray(Allocator.Temp);
        var sources = q.ToComponentDataArray<SpellSource>(Allocator.Temp);
        var doomed = new NativeList<Entity>(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
            if (sources[i].CasterEntity == caster && (dbIndex < 0 || sources[i].DatabaseIndex == dbIndex))
                doomed.Add(entities[i]);

        int count = doomed.Length;
        if (count > 0)
            em.DestroyEntity(doomed.AsArray());

        doomed.Dispose();
        sources.Dispose();
        entities.Dispose();
        return count;
    }

    // ---------------- Characters ----------------

    /// <summary>CharactersDatabaseSO.Characters, whose index is the baked CharacterPrefabBufferElement index
    /// (CharactersDatabaseAuthoring). Editor: AssetDatabase; development build: the instance loaded by SC_Main.</summary>
    public static CharacterSO[] GetCharacters()
    {
        CharactersDatabaseSO db = null;
#if UNITY_EDITOR
        var guids = UnityEditor.AssetDatabase.FindAssets("t:CharactersDatabaseSO");
        if (guids.Length > 0)
            db = UnityEditor.AssetDatabase.LoadAssetAtPath<CharactersDatabaseSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
        if (db == null)
            db = Resources.FindObjectsOfTypeAll<CharactersDatabaseSO>().FirstOrDefault();
        return db != null && db.Characters != null ? db.Characters : Array.Empty<CharacterSO>();
    }

    public static string CharacterLabel(int dbIndex)
    {
        var chars = GetCharacters();
        if (dbIndex < 0 || dbIndex >= chars.Length || chars[dbIndex] == null)
            return $"#{dbIndex}";
        return string.IsNullOrEmpty(chars[dbIndex].DisplayName) ? chars[dbIndex].name : chars[dbIndex].DisplayName;
    }

    /// <summary>Stable key stored in presets: the CharacterSO asset name.</summary>
    public static string CharacterKey(int dbIndex)
    {
        var chars = GetCharacters();
        return dbIndex >= 0 && dbIndex < chars.Length && chars[dbIndex] != null ? chars[dbIndex].name : string.Empty;
    }

    /// <summary>A DB index, or an asset name / display name (exact, then substring).</summary>
    public static bool TryResolveCharacter(string query, out int dbIndex, out string label, out string error)
    {
        dbIndex = -1;
        label = null;
        error = null;
        var chars = GetCharacters();
        if (chars.Length == 0)
        {
            error = "Base de persos introuvable.";
            return false;
        }

        if (int.TryParse(query, out var idx))
        {
            if (idx < 0 || idx >= chars.Length)
            {
                error = $"Index de perso hors limites (0..{chars.Length - 1}).";
                return false;
            }
            dbIndex = idx;
        }
        else
        {
            var indices = Enumerable.Range(0, chars.Length).Where(i => chars[i] != null);
            if (!TryResolve(query, indices, i => new[] { chars[i].name, chars[i].DisplayName }, CharacterLabel, out dbIndex, out error))
            {
                dbIndex = -1;
                return false;
            }
        }

        label = CharacterLabel(dbIndex);
        return true;
    }

    // ---------------- Sandbox enemy registry ----------------

    public static bool TryGetRegistry(EntityManager em, out Entity registry)
    {
        registry = Entity.Null;
        using var q = em.CreateEntityQuery(typeof(SandboxRegistry));
        if (q.CalculateEntityCount() != 1)
            return false;
        registry = q.GetSingletonEntity();
        return true;
    }

    public static List<string> EnemyNames(EntityManager em)
    {
        var names = new List<string>();
        if (!TryGetRegistry(em, out var reg) || !em.HasBuffer<SandboxEnemyPrefab>(reg))
            return names;
        foreach (var e in em.GetBuffer<SandboxEnemyPrefab>(reg, true))
            names.Add(e.Name.ToString());
        return names;
    }

    public static bool TryResolveEnemyPrefab(EntityManager em, string query, out Entity prefab, out string name, out string error)
    {
        prefab = Entity.Null;
        name = null;
        if (!TryGetRegistry(em, out var reg) || !em.HasBuffer<SandboxEnemyPrefab>(reg))
        {
            error = SandboxOnly;
            return false;
        }

        var entries = new List<(string Name, Entity Prefab)>();
        foreach (var e in em.GetBuffer<SandboxEnemyPrefab>(reg, true))
            entries.Add((e.Name.ToString(), e.Prefab));

        if (!TryResolve(query, entries, e => new[] { e.Name }, e => e.Name, out var match, out error))
            return false;
        prefab = match.Prefab;
        name = match.Name;
        return true;
    }
}
#endif
