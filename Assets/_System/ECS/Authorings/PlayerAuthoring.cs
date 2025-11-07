using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    [Header("Movement precision")]
    [Tooltip("If true, the player will be snapped perfectyl on the ground following the terrain height. Otherwise, it will follow the base radius.")]
    public bool UseSnappedMovement = true;

    [Header("Stats \n" +
        "Resistances are value in %.")]
    public BaseStats BaseStats;

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

            AddComponent(entity, new Health() { Value = authoring.BaseStats.MaxHealth });

            AddComponent(entity, new LinearMovement()
            {
                Direction = float3.zero,
                Speed = authoring.BaseStats.Speed
            });

            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddBuffer<DamageBufferElement>(entity);

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

            AddComponent<RecalculateStatsRequest>(entity);

            DynamicBuffer<ActiveSpell> spellBuffer = AddBuffer<ActiveSpell>(entity);

            DynamicBuffer<SpellActivationRequest> baseSpellBuffer = AddBuffer<SpellActivationRequest>(entity);
            foreach (var spellSO in authoring.InitialSpells)
            {
                if (spellSO == null || spellSO.SpellPrefab == null)
                    continue;

                baseSpellBuffer.Add(new SpellActivationRequest
                {
                    ID = spellSO.ID,
                });
            }

            var expBuffer = AddBuffer<CollectedExperienceBufferElement>(entity);

            AddComponent(entity, new PlayerExperience()
            {
                Experience = 0,
                Level = 1,
                NextLevelExperienceRequired = 500
            });
        }
    }
}
