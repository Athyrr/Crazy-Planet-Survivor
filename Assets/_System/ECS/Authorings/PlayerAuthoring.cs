using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    [Header("Stats")]
    public BaseStats BaseStats = new BaseStats()
    {
        MaxHealth = 100,
        Damage = 100,
        Armor = 5,
        Speed = 2
    };

    [Header("Spells")]
    public SpellDataSO[] InitialSpells;

    [Header("Modifiers")]
    public StatModifier[] InitialModifers;

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Player() { });

            AddComponent(entity, new InputData() { Value = new float2(0, 0) });


            //SetComponent(entity, new LocalTransform()
            //{
            //    Position = authoring.transform.position,
            //    Rotation = authoring.transform.rotation,
            //    Scale = 1
            //});

            AddComponent(entity, new LinearMovement() // @todo Movement system read player speed stats.
            {
                Direction = float3.zero,
                Speed = authoring.BaseStats.Speed
            });

            AddComponent(entity, authoring.BaseStats);

            AddComponent(entity, new Stats()
            {
                MaxHealth = authoring.BaseStats.MaxHealth,
                Speed = authoring.BaseStats.Speed,
                Damage = authoring.BaseStats.Damage,
                Armor = authoring.BaseStats.Armor,
                CooldownReduction = authoring.BaseStats.CooldownReduction
            });

            DynamicBuffer<StatModifier> modifierBuffer = AddBuffer<StatModifier>(entity);
            foreach (var modifier in authoring.InitialModifers)
                modifierBuffer.Add(modifier);

            DynamicBuffer<ActiveSpell> spellBuffer = AddBuffer<ActiveSpell>(entity);

            DynamicBuffer<BaseSpell> baseSpellBuffer = AddBuffer<BaseSpell>(entity);
            foreach (var spellSO in authoring.InitialSpells)
            {
                if (spellSO == null || spellSO.SpellPrefab == null)
                    continue;

                baseSpellBuffer.Add(new BaseSpell
                {
                    ID = spellSO.ID,
                });

            }
        }
    }
}
