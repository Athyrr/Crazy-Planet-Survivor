using UnityEngine;

[CreateAssetMenu(fileName = "CharactersDatabase", menuName = "Survivor/Databases/Characters")]
public class CharactersDatabaseSO : ScriptableObject
{
    public CharacterSO[] Characters;

#if UNITY_EDITOR
    [EasyButtons.Button("Populate (find all Characters)")]
    public void Populate()
    {
        Characters = DatabaseAutoPopulateUtils.FindAllAssets<CharacterSO>();
        DatabaseAutoPopulateUtils.Save(this);
        Debug.Log($"[{name}] Populated {Characters.Length} characters.", this);
    }
#endif
}
