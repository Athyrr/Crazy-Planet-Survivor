using UnityEngine;

[CreateAssetMenu(fileName = "SpellsDatabase", menuName = "Survivor/Databases/Spells")]
public class SpellDatabaseSO : ScriptableObject
{
    public SpellSO[] Spells;

#if UNITY_EDITOR
    [EasyButtons.Button("Populate (find all Spells)")]
    public void Populate()
    {
        Spells = DatabaseAutoPopulateUtils.FindAllAssets<SpellSO>();
        DatabaseAutoPopulateUtils.Save(this);
        Debug.Log($"[{name}] Populated {Spells.Length} spells.", this);
    }
#endif
}
