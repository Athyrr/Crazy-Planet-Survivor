using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    [Header("Datas")]
    [Tooltip("The character's base stats and initial spells.")]
    public CharacterDataSO CharacterData;

    [Header("Movement Settings")]
    [Tooltip("If true, the player will be snapped perfectly on the ground following the terrain height.")]
    public bool UseSnappedMovement = true;

    [Header("Debug")]
    [Tooltip("Modifiers to apply on spawn (e.g. for testing specific builds).")]
    public StatModifier[] InitialModifiers;

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            if (authoring.CharacterData == null)
            {
                Debug.LogError($"PlayerAuthoring on {authoring.name} is missing CharacterDataSO!", authoring);
                return;
            }

            var baseStats = authoring.CharacterData.BaseStats;
            var initialSpells = authoring.CharacterData.InitialSpells;

            AddComponent(entity, new Player() { });
            AddComponent(entity, new InputData() { Value = new float2(0, 0) });

            AddComponent(entity, new Health { Value = baseStats.MaxHealth });

            AddComponent(entity, new LinearMovement
            {
                Direction = float3.zero,
                Speed = baseStats.MoveSpeed
            });

            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddBuffer<DamageBufferElement>(entity);

            // Base Stats
            AddComponent<BaseStats>(entity, baseStats);

            // Initialize dynamic Stats with base values
            AddComponent(entity, new Stats
            {
                MaxHealth = baseStats.MaxHealth,
                Armor = baseStats.Armor,

                MoveSpeed = baseStats.MoveSpeed,

                Damage = baseStats.Damage,
                CooldownReduction = baseStats.CooldownReduction,

                ProjectileSpeedMultiplier = baseStats.ProjectileSpeedMultiplier,
                EffectAreaRadiusMult = baseStats.EffectAreaRadiusMultiplier,
                BouncesAdded = baseStats.BouncesAdded,
                PierceAdded = baseStats.PierceAdded,

                FireResistance = baseStats.FireResistance,
                IceResistance = baseStats.IceResistance,
                LightningResistance = baseStats.LightningResistance,
                ArcaneResistance = baseStats.ArcaneResistance,

                CollectRange = baseStats.CollectRange,
                MaxCollectRange = baseStats.MaxCollectRange
            });

            // Stat Modifiers
            DynamicBuffer<StatModifier> statModifierBuffer = AddBuffer<StatModifier>(entity);
            if (authoring.InitialModifiers != null && authoring.InitialModifiers.Length > 0)
            {
                foreach (var modifier in authoring.InitialModifiers)
                    statModifierBuffer.Add(modifier);
            }
            // Request to reacalultate stats using stat modfiers values.
            AddComponent<RecalculateStatsRequest>(entity);

            // Spells
            AddBuffer<ActiveSpell>(entity);
            DynamicBuffer<SpellActivationRequest> spellActivationBuffer = AddBuffer<SpellActivationRequest>(entity);

            if (initialSpells != null)
            {
                foreach (var spellSO in initialSpells)
                {
                    if (spellSO == null || spellSO.SpellPrefab == null)
                        continue;

                    spellActivationBuffer.Add(new SpellActivationRequest
                    {
                        ID = spellSO.ID,
                    });
                }
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
