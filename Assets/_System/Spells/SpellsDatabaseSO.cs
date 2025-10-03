using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "SpellDatabase", menuName = "Survivor/Spell Database")]
public class SpellDatabaseSO : ScriptableObject
{
    public SpellDataSO[] Spells;
}