#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

/// <summary>One read of everything the sandbox window and measures show (scene-de-test), copied into managed data.
/// Every EntityManager read completes the jobs writing that type: call it at ~4 Hz, never per frame.</summary>
public static class SandboxMetrics
{
    public struct DummyRow
    {
        public Entity Entity;
        public float Total, MaxHit;
        public int Hits;
        public int Health;
        public float MaxHealth, Armor;
    }

    public struct SpellRow
    {
        public ESpellID Id;
        public int DatabaseIndex;
        public float TotalDamage;   // ActiveSpell.TotalDamageDealt: BEFORE armor
        public int FinalBounces, FinalPierces;
        public ESpellCapability Granted, Allowed;
    }

    public sealed class Snapshot
    {
        public bool Valid;
        public double Time;
        public EGameState State;
        public bool IsSandbox;
        public int CharacterIndex = -1;
        public string Character = string.Empty;
        public bool HasPlayer;
        public int PlayerHealth;
        public CoreStats PlayerStats;
        public bool HasSettings;
        public SandboxSettings Settings;
        public readonly List<DummyRow> Dummies = new List<DummyRow>();
        public readonly List<SpellRow> Spells = new List<SpellRow>();
        public int MaxEnemies = -1;
        public bool HasDirector;
        public bool DirectorEnabled;
        public int ActiveEnemyCount;
        public readonly List<string> EnemyTypes = new List<string>();
    }

    public static Snapshot Capture()
    {
        var snap = new Snapshot();
        try
        {
            if (!SandboxCommandUtils.TryGetWorld(out var world, out var em))
                return snap;

            snap.Time = world.Time.ElapsedTime;
            snap.State = SandboxCommandUtils.GetGameState(em);
            snap.IsSandbox = SandboxCommandUtils.IsSandboxPlanet(em);

            using (var sel = em.CreateEntityQuery(typeof(SelectedCharacter)))
                if (sel.TryGetSingleton<SelectedCharacter>(out var selected))
                {
                    snap.CharacterIndex = selected.DbIndex;
                    snap.Character = SandboxCommandUtils.CharacterLabel(selected.DbIndex);
                }

            using (var sq = em.CreateEntityQuery(typeof(SandboxSettings)))
                snap.HasSettings = sq.TryGetSingleton(out snap.Settings);

            if (SandboxCommandUtils.TryGetPlayer(out _, out var player))
            {
                snap.HasPlayer = true;
                snap.PlayerHealth = em.GetComponentData<Health>(player).Value;
                snap.PlayerStats = em.GetComponentData<CoreStats>(player);
                bool hasDb = SandboxCommandUtils.TryGetSpellsDb(em, out var db);
                foreach (var s in em.GetBuffer<ActiveSpell>(player, true))
                {
                    var row = new SpellRow
                    {
                        DatabaseIndex = s.DatabaseIndex,
                        TotalDamage = s.TotalDamageDealt,
                        FinalBounces = s.FinalBounces,
                        FinalPierces = s.FinalPierces,
                        Granted = s.GrantedCapabilities,
                    };
                    if (hasDb && s.DatabaseIndex >= 0 && s.DatabaseIndex < db.Value.Spells.Length)
                    {
                        row.Id = db.Value.Spells[s.DatabaseIndex].ID;
                        row.Allowed = db.Value.Spells[s.DatabaseIndex].AllowedCapabilities;
                    }
                    snap.Spells.Add(row);
                }
            }

            using (var dq = em.CreateEntityQuery(typeof(DummyTag), typeof(DamageTally), typeof(Health), typeof(CoreStats)))
            {
                dq.CompleteDependency();
                var entities = dq.ToEntityArray(Allocator.Temp);
                var tallies = dq.ToComponentDataArray<DamageTally>(Allocator.Temp);
                var healths = dq.ToComponentDataArray<Health>(Allocator.Temp);
                var cores = dq.ToComponentDataArray<CoreStats>(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                    snap.Dummies.Add(new DummyRow
                    {
                        Entity = entities[i], Total = tallies[i].Total, MaxHit = tallies[i].MaxHit, Hits = tallies[i].Hits,
                        Health = healths[i].Value, MaxHealth = cores[i].MaxHealth, Armor = cores[i].BaseArmor + cores[i].Armor,
                    });
                cores.Dispose(); healths.Dispose(); tallies.Dispose(); entities.Dispose();
            }

            using (var spq = em.CreateEntityQuery(typeof(SpawnerSettings)))
                if (spq.TryGetSingleton<SpawnerSettings>(out var spawner))
                    snap.MaxEnemies = spawner.MaxEnemies;
            using (var stq = em.CreateEntityQuery(typeof(SpawnerState)))
                if (stq.TryGetSingleton<SpawnerState>(out var st))
                    snap.ActiveEnemyCount = st.ActiveEnemyCount;
            using (var dirq = em.CreateEntityQuery(typeof(SpawnIntensityConfig)))
                if (dirq.TryGetSingleton<SpawnIntensityConfig>(out var director))
                {
                    snap.HasDirector = true;
                    snap.DirectorEnabled = director.Enabled;
                }

            if (snap.IsSandbox)
                snap.EnemyTypes.AddRange(SandboxCommandUtils.EnemyNames(em));

            snap.Valid = true;
        }
        catch (ObjectDisposedException)
        {
            snap.Valid = false; // world torn down while exiting Play
        }
        catch (InvalidOperationException)
        {
            snap.Valid = false;
        }
        return snap;
    }

    /// <summary>dps_reset: dummy tallies, the player's tally and every ActiveSpell.TotalDamageDealt back to zero.</summary>
    public static void ResetCounters(EntityManager em)
    {
        using (var q = em.CreateEntityQuery(typeof(DamageTally)))
        {
            q.CompleteDependency();
            var entities = q.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
                em.SetComponentData(e, default(DamageTally));
            entities.Dispose();
        }

        if (SandboxCommandUtils.TryGetPlayer(out _, out var player))
        {
            var spells = em.GetBuffer<ActiveSpell>(player);
            for (int i = 0; i < spells.Length; i++)
            {
                var s = spells[i];
                s.TotalDamageDealt = 0f;
                spells[i] = s;
            }
        }
    }
}

/// <summary>Rolling DPS over a world-time window, fed with cumulative totals (DamageTally.Total). A total that goes down
/// (dps_reset, respawned dummy) restarts the window.</summary>
public sealed class SandboxDpsTracker
{
    public float WindowSeconds = 5f;

    private readonly Dictionary<Entity, Queue<(double time, float total)>> _samples = new Dictionary<Entity, Queue<(double, float)>>();
    private readonly Dictionary<Entity, (double time, float total)> _last = new Dictionary<Entity, (double, float)>();

    public void Add(Entity key, double time, float total)
    {
        if (!_samples.TryGetValue(key, out var q))
            _samples[key] = q = new Queue<(double, float)>();
        if (_last.TryGetValue(key, out var last) && total < last.total)
            q.Clear();
        q.Enqueue((time, total));
        _last[key] = (time, total);
        while (q.Count > 1 && time - q.Peek().time > WindowSeconds)
            q.Dequeue();
    }

    public float Dps(Entity key)
    {
        if (!_samples.TryGetValue(key, out var q) || q.Count < 2 || !_last.TryGetValue(key, out var last))
            return 0f;
        var first = q.Peek();
        double dt = last.time - first.time;
        return dt > 0 ? (float)((last.total - first.total) / dt) : 0f;
    }

    public void Clear()
    {
        _samples.Clear();
        _last.Clear();
    }
}
#endif
