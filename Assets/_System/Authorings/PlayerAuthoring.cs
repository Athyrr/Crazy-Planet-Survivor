using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine.InputSystem.Android;

public class PlayerAuthoring : MonoBehaviour
{
    [Header("Movement Settings \n" +
        "@todo use base stats instead")]
    public float BaseSpeed = 1.0f;

    [Header("Stats")]
    public BaseStats BaseStats = new BaseStats()
    {
        MaxHealth = 100,
        Damage = 100,
        Armor = 5,
        Speed = 2
    };

    [Header("Spells \n" +
        "@todo ScriptAsset for spell refs and convert in authoring")]
    public ActiveSpell[] InitialSpells;

    [Header("Modifiers")]
    public StatModifier[] InitialModifers;

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Player() { });

            AddComponent(entity, new InputData() { Value = new float2(0, 0) });

            AddComponent(entity, new LocalTransform()
            {
                Position = authoring.transform.position,
                Rotation = authoring.transform.rotation,
                Scale = 1
            });

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
            {
                modifierBuffer.Add(modifier);
            }

            DynamicBuffer<ActiveSpell> spellBuffer = AddBuffer<ActiveSpell>(entity);
            foreach (var spell in authoring.InitialSpells)
            {
                spellBuffer.Add(spell);
            }

        }
    }
}
