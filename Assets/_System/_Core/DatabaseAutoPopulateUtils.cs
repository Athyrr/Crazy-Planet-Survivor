#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only helpers shared by the game databases' "Populate" buttons.
/// Scans the whole project for every asset of a given ScriptableObject type so that
/// databases never go out of sync when new data assets are created.
/// </summary>
public static class DatabaseAutoPopulateUtils
{
    /// <summary>
    /// Finds every asset of type <typeparamref name="T"/> in the project (including subtypes),
    /// optionally filtered, sorted by asset name for a stable, deterministic order.
    /// </summary>
    public static T[] FindAllAssets<T>(Func<T, bool> filter = null) where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        var results = new List<T>(guids.Length);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                continue;
            if (filter != null && !filter(asset))
                continue;

            results.Add(asset);
        }

        results.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return results.ToArray();
    }

    /// <summary>
    /// Marks the database dirty and writes it to disk.
    /// </summary>
    public static void Save(ScriptableObject database)
    {
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssetIfDirty(database);
    }
}
#endif
