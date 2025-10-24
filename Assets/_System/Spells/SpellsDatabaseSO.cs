using UnityEngine;

[CreateAssetMenu(fileName = "SpellDatabase", menuName = "Survivor/Spell/Spell Database")]
public class SpellDatabaseSO : ScriptableObject
{
    public SpellDataSO[] Spells;
}