using _System.ECS.Authorings.Ressources;
using Unity.Entities;
using UnityEngine;

public class LootAuthoring : MonoBehaviour
{
    #region Members
    
    [SerializeField] private ERessourceType _ressourceType;    
    [SerializeField] [Min(0)] private int _value;

    [SerializeField] [Range(0f, 1f)] private float _dropChance = 1.0f;
    
    #endregion

    #region Accessors
    
    public ERessourceType RessourceType => _ressourceType;    
    public int Value => _value;

    #endregion
    
    
    private class Baker : Baker<LootAuthoring>
    {
        public override void Bake(LootAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new Loot
            {
                Type = authoring._ressourceType,
                Value = authoring._value,
                DropChance = authoring._dropChance
            });
        }
    }
}
