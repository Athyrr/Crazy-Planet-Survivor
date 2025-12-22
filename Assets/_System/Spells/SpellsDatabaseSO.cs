using UnityEngine;

[CreateAssetMenu(fileName = "SpellsDatabase", menuName = "Survivor/Databases/Spells")]
public class SpellDatabaseSO : ScriptableObject
{
    public SpellDataSO[] Spells;
}