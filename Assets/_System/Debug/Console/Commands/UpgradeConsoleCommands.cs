using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;

public static class UpgradeConsoleCommands
{
    private readonly struct Entry
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

    [ConsoleCommand("give_upgrade", "Apply an upgrade to the player as if picked at level-up (asset or display name, quote if spaces).")]
    private static string GiveUpgrade(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Usage: give_upgrade <name>";
        if (!TryLoadEntries(out var entries, out var error))
            return error;

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        using var playerQuery = em.CreateEntityQuery(typeof(Player));
        if (playerQuery.IsEmpty)
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

        var playerEntity = playerQuery.GetSingletonEntity();
        var request = new ApplyUpgradeRequest { DatabaseIndex = matches[0].DatabaseIndex };
        if (em.HasComponent<ApplyUpgradeRequest>(playerEntity))
            em.SetComponentData(playerEntity, request);
        else
            em.AddComponentData(playerEntity, request);

        return $"Applied upgrade {matches[0].Label} (index {matches[0].DatabaseIndex}).";
    }

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
