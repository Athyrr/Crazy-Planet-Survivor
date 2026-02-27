using UnityEngine;

[CreateAssetMenu(fileName = "AmuletDatabase", menuName = "Survivor/Databases/Amulets")]
public class AmuletsDatabaseSO : ScriptableObject
{
    public AmuletSO[] Amulets;
}
