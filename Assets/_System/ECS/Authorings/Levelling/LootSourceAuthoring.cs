using _System.ECS.Authorings.Resources;
using Unity.Entities;
using UnityEngine;

public class LootSourceAuthoring : MonoBehaviour
{
    #region Members

    [SerializeField] private EResourceType _resourceType;
    [SerializeField] [Min(0)] private int _value;
    [SerializeField] private bool _isExperience;
    [SerializeField] [Range(0f, 1f)] private float _dropChance = 1.0f;

    #endregion

    #region Accessors

    public EResourceType ResourceType => _resourceType;
    public int Value => _value;
    public bool IsExperience => _isExperience;

    #endregion

    private class Baker : Baker<LootSourceAuthoring>
    {
        public override void Bake(LootSourceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new LootSource
            {
                Type = authoring._resourceType,
                Value = authoring._value,
                IsExperience = authoring._isExperience,
                DropChance = authoring._dropChance
            });
        }
    }
}
