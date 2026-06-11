using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu to (re)populate every game database in one click, so databases never go
/// out of sync when new data assets are created.
/// </summary>
public static class DatabasesMenu
{
    [MenuItem("Survivor/Databases/Populate All Databases")]
    public static void PopulateAll()
    {
        PopulateEach<SpellDatabaseSO>(db => db.Populate());
        PopulateEach<AmuletsDatabaseSO>(db => db.Populate());
        PopulateEach<CharactersDatabaseSO>(db => db.Populate());
        PopulateEach<MetaUpgradesDatabaseSO>(db => db.Populate());
        PopulateEach<ResourceDatabaseSO>(db => db.Populate());
        PopulateEach<UpgradesDatabaseSO>(db => db.Populate());

        AssetDatabase.SaveAssets();
        Debug.Log("[Databases] Populate All Databases complete.");
    }

    private static void PopulateEach<T>(Action<T> populate) where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var db = AssetDatabase.LoadAssetAtPath<T>(path);
            if (db != null)
                populate(db);
        }
    }
}
