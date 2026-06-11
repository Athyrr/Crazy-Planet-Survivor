using UnityEngine;

[CreateAssetMenu(fileName = "AmuletDatabase", menuName = "Survivor/Databases/Amulets")]
public class AmuletsDatabaseSO : ScriptableObject
{
    public AmuletSO[] Amulets;

#if UNITY_EDITOR
    [EasyButtons.Button("Populate (find all Amulets)")]
    public void Populate()
    {
        Amulets = DatabaseAutoPopulateUtils.FindAllAssets<AmuletSO>();
        DatabaseAutoPopulateUtils.Save(this);
        Debug.Log($"[{name}] Populated {Amulets.Length} amulets.", this);
    }
#endif
}
