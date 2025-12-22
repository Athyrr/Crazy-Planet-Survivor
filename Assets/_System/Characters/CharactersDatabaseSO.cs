using UnityEngine;

[CreateAssetMenu(fileName = "CharactersDatabase", menuName = "Survivor/Databases/Characters")]
public class CharactersDatabaseSO : ScriptableObject
{
    public CharacterDataSO[] Characters;
}
