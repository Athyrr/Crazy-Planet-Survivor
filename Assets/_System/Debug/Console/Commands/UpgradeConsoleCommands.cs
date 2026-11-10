using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;

public static class UpgradeConsoleCommands
{
    public readonly struct Entry
    {
        public readonly int DatabaseIndex;
        public readonly string AssetName;
        public readonly string DisplayName;

        public Entry(int databaseIndex, string assetName, string displayName)
        {
            DatabaseIndex = databaseIndex;
            AssetName = assetName;
            DisplayName = displayName;
        }

        public string Label =>
            string.IsNullOrEmpty(AssetName) || AssetName == DisplayName ? DisplayName : $"{AssetName} ({DisplayName})";

        public bool Matches(string filter) =>
            AssetName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
            DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        public bool Equals(string name) =>
            string.Equals(AssetName, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(DisplayName, name, StringComparison.OrdinalIgnoreCase);
    }

    [ConsoleCommand("list_upgrades", "List upgrade names matching an optional filter.")]
    private static string ListUpgrades(string filter = "")
    {
        if (!TryLoadEntries(out var entries, out var error))
            return error;

        var sb = new StringBuilder();
        int count = 0;
        foreach (var e in entries)
        {
            if (!string.IsNullOrEmpty(filter) && !e.Matches(filter)) continue;
            sb.AppendLine($"  {e.Label}");
            count++;
        }
        return count == 0 ? $"No upgrade matches '{filter}'." : $"{count} upgrade(s):\n{sb.ToString().TrimEnd()}";
    }

    /// <summary>Upgrade names and DB indices, in baked-blob order (used by the sandbox window and presets).</summary>
    public static bool TryGetUpgradeEntries(out List<Entry> entries, out string error) => TryLoadEntries(out entries, out error);

    [ConsoleCommand("give_upgrade", "Apply an upgrade to the player N times, one per frame (asset or display name, quote if spaces). Keeps an open upgrade screen.")]
    private static string GiveUpgrade(string name, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Usage: give_upgrade <name> [count]";
        if (count < 1)
            return "Count must be >= 1.";
        if (!TryLoadEntries(out var entries, out var error))
            return error;

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        using var playerQuery = em.CreateEntityQuery(typeof(Player));
        if (playerQuery.CalculateEntityCount() != 1)
            return "No Player entity found. Are you in a run?";

        // Exact (asset/display, case-insensitive) first, then substring.
        var matches = new List<Entry>();
        foreach (var e in entries) if (e.Equals(name)) matches.Add(e);
        if (matches.Count == 0)
            foreach (var e in entries) if (e.Matches(name)) matches.Add(e);

        if (matches.Count == 0)
            return $"No upgrade matches '{name}'. Try list_upgrades.";
        if (matches.Count > 1)
        {
            var sb = new StringBuilder($"'{name}' is ambiguous, candidates:");
            foreach (var m in matches) sb.Append($"\n  {m.Label}");
            return sb.ToString();
        }

        var match = matches[0];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        SandboxSession.RecordUpgrade(match.AssetName, count);
        if (count > 1)
        {
            SandboxRunner.Run(GiveRepeated(match.DatabaseIndex, match.Label, count));
            return $"{match.Label}: {count} applications queued (one per frame).";
        }
#else
        if (count > 1)
            return "give_upgrade count > 1 needs a development build.";
#endif
        Post(em, playerQuery.GetSingletonEntity(), match.DatabaseIndex);
        return $"Applied upgrade {match.Label} (index {match.DatabaseIndex}).";
    }

    private static void Post(EntityManager em, Entity player, int databaseIndex)
    {
        var request = new ApplyUpgradeRequest { DatabaseIndex = databaseIndex, KeepSelection = true };
        if (em.HasComponent<ApplyUpgradeRequest>(player))
            em.SetComponentData(player, request);
        else
            em.AddComponentData(player, request);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // ApplyUpgradeRequest is a single component, overwritten by a second post in the same frame: post the next one
    // only once ApplyUpgradeSystem consumed the previous one.
    private static System.Collections.IEnumerator GiveRepeated(int databaseIndex, string label, int count)
    {
        int applied = 0;
        while (applied < count)
        {
            if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            {
                SandboxRunner.Report($"give_upgrade {label}: player lost after {applied}/{count}.");
                yield break;
            }
            if (!em.HasComponent<ApplyUpgradeRequest>(player))
            {
                Post(em, player, databaseIndex);
                applied++;
            }
            yield return null;
        }
        SandboxRunner.Report($"give_upgrade {label}: {count} application(s).");
    }
#endif

    // Indices must match the baked UpgradesDatabase blob (same order as GameUpgradesConfigSO.GetFlattenedUpgrades).
    private static bool TryLoadEntries(out List<Entry> entries, out string error)
    {
        entries = new List<Entry>();
        error = null;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            error = "No ECS world.";
            return false;
        }

        using var dbQuery = world.EntityManager.CreateEntityQuery(typeof(UpgradesDatabase));
        if (dbQuery.IsEmpty)
        {
            error = "No UpgradesDatabase singleton (subscene not loaded?).";
            return false;
        }

        var blobs = dbQuery.GetSingleton<UpgradesDatabase>().Blobs;
        ref var upgrades = ref blobs.Value.Upgrades;

#if UNITY_EDITOR
        var guids = UnityEditor.AssetDatabase.FindAssets("t:GameUpgradesConfigSO");
        if (guids.Length > 0)
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameUpgradesConfigSO>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            var flat = config != null ? config.GetFlattenedUpgrades() : null;
            if (flat != null && flat.Count == upgrades.Length)
            {
                for (int i = 0; i < flat.Count; i++)
                    entries.Add(new Entry(i, flat[i].name, upgrades[i].DisplayName.ToString()));
                return true;
            }
        }
#endif

        for (int i = 0; i < upgrades.Length; i++)
        {
            var display = upgrades[i].DisplayName.ToString();
            entries.Add(new Entry(i, display, display));
        }
        return true;
    }
}
