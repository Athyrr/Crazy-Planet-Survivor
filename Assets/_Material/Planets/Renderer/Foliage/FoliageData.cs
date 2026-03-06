// FoliageData.cs
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Foliage/FoliageData", fileName = "FoliageData")]
public class FoliageData : ScriptableObject
{
    public List<FoliageInstance> instances = new List<FoliageInstance>();
}