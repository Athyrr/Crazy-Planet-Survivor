using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    private GameUpgradesConfigSO _gameUpgradesConfig;

    [Header("Datas")]
    [Tooltip("The character's base stats and initial spells.")]
    public CharacterSO characterData;

    [Header("Movement Settings")]
    [Tooltip(
        "If true, the player will be snapped perfectly on the ground following the terrain height."
    )]
    public bool UseSnappedMovement = true;

    [Header("Debug")]
    [Tooltip("If true, the player will be invincible.")]
    public bool IsInvincible = false;

    [Tooltip("Modifiers to apply on spawn (e.g. for testing specific builds).")]
    public StatModifier[] InitialModifiers;

    [Tooltip("Upgrades to apply on spawn (e.g. for testing specific builds).")]
    public UpgradeSO[] InitialUpgrades;

    private void OnValidate()
    {
        // Load GameUpgradesConfigSO from project

        if (_gameUpgradesConfig == null)
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:GameUpgradesConfigSO");

            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                _gameUpgradesConfig =
                    UnityEditor.AssetDatabase.LoadAssetAtPath<GameUpgradesConfigSO>(path);

                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            }
            else
            {
                Debug.LogWarning(
                    "[PlayerAuthoring] 'GlobalUpgradesConfigSO' not found in project!"
                );
            }
#endif
        }
    }

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            if (authoring.characterData == null)
            {
                Debug.LogError(
                    $"PlayerAuthoring on {authoring.name} is missing CharacterDataSO!",
                    authoring
                );
                return;
            }

            if (authoring._gameUpgradesConfig == null)
            {
                Debug.LogError(
                    $"[PlayerAuthoring] '{authoring.name}' needs a reference to GameUpgradesConfigSO.",
                    authoring
                );
                return;
            }

            List<UpgradeSO> gameUpgradesList = authoring._gameUpgradesConfig.GetFlattenedUpgrades();

            var baseStats = authoring.characterData.BaseStats;
            var initialSpells = authoring.characterData.InitialSpells;

            AddComponent(entity, new Player() { });
            AddComponent(entity, new InputData() { Value = new float2(0, 0) });
            AddComponent(entity, new Health { Value = baseStats.MaxHealth });
            AddComponent(
                entity,
                new LinearMovement { Direction = float3.zero, Speed = baseStats.MoveSpeed }
            );

            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddBuffer<DamageBufferElement>(entity);

            // Base Stats
            AddComponent<BaseStats>(entity, baseStats);

            // Initialize dynamic Stats with base values
            AddComponent(
                entity,
                new Stats
                {
                    MaxHealth = baseStats.MaxHealth,
                    Armor = baseStats.Armor,

                    MoveSpeed = baseStats.MoveSpeed,

                    Damage = baseStats.Damage,
                    CooldownReduction = baseStats.CooldownReduction,
                    CritChance = baseStats.CritChance,
                    CritMultiplier = baseStats.CritMultiplier,

                    ProjectileSpeedMultiplier = baseStats.ProjectileSpeedMultiplier,
                    EffectAreaRadiusMult = baseStats.EffectAreaRadiusMultiplier,
                    BouncesAdded = baseStats.BouncesAdded,
                    PierceAdded = baseStats.PierceAdded,

                    FireResistance = baseStats.FireResistance,
                    IceResistance = baseStats.IceResistance,
                    LightningResistance = baseStats.LightningResistance,
                    PoisonResistance = baseStats.PoisonResistance,
                    LightResistance = baseStats.LightResistance,
                    DarkResistance = baseStats.DarkResistance,
                    NatureResistance = baseStats.NatureResistance,

                    SizeMult = baseStats.SizeMultiplier,

                    CollectRange = baseStats.CollectRange,
                    MaxCollectRange = baseStats.MaxCollectRange,
                }
            );

            // Stat Modifiers
            DynamicBuffer<StatModifier> statModifierBuffer = AddBuffer<StatModifier>(entity);
            if (authoring.InitialModifiers != null && authoring.InitialModifiers.Length > 0)
            {
                foreach (var modifier in authoring.InitialModifiers)
                    statModifierBuffer.Add(modifier);
            }

            // Request to reacalultate stats using stat modfiers values.
            AddComponent<RecalculateStatsRequest>(entity);

            // Base spells
            var baseSpells = AddBuffer<BaseSpell>(entity);

            // Active Spells
            AddBuffer<ActiveSpell>(entity);
            DynamicBuffer<SpellActivationRequest> spellActivationBuffer =
                AddBuffer<SpellActivationRequest>(entity);

            if (initialSpells != null)
            {
                foreach (var spellSO in initialSpells)
                {
                    if (spellSO == null || spellSO.SpellPrefab == null)
                        continue;

                    spellActivationBuffer.Add(new SpellActivationRequest { ID = spellSO.ID });

                    baseSpells.Add(new BaseSpell { ID = spellSO.ID });
                }
            }

            // Stats Upgrade Pool
            DynamicBuffer<StatsUpgradePoolBufferElement> statsUpgradebuffer =
                AddBuffer<StatsUpgradePoolBufferElement>(entity);
            if (authoring.characterData.StatsUpgradesPool != null)
            {
                foreach (var localUpgrade in authoring.characterData.StatsUpgradesPool.Upgrades)
                {
                    if (localUpgrade == null)
                        continue;

                    // Get the index of this upgrade in the global list
                    int globalIndex = gameUpgradesList.IndexOf(localUpgrade);

                    if (globalIndex != -1)
                    {
                        statsUpgradebuffer.Add(
                            new StatsUpgradePoolBufferElement { DatabaseIndex = globalIndex }
                        );
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Upgrade '{localUpgrade.name}' is in CharacterData but NOT in the Global Database Authoring. Add it to the Global Database scene object.",
                            authoring
                        );
                    }
                }
            }

            // Spells Upgrade Pool
            DynamicBuffer<SpellsUpgradePoolBufferElement> spellsUpgradebuffer =
                AddBuffer<SpellsUpgradePoolBufferElement>(entity);
            if (authoring.characterData.SpellUpgradesPool != null)
            {
                foreach (var localUpgrade in authoring.characterData.SpellUpgradesPool.Upgrades)
                {
                    if (localUpgrade == null)
                        continue;

                    int globalIndex = gameUpgradesList.IndexOf(localUpgrade);

                    if (globalIndex != -1)
                    {
                        spellsUpgradebuffer.Add(
                            new SpellsUpgradePoolBufferElement { DatabaseIndex = globalIndex }
                        );
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Upgrade '{localUpgrade.name}' is in CharacterData but NOT in the Global Database Authoring.",
                            authoring
                        );
                    }
                }
            }

            var expBuffer = AddBuffer<CollectedExperienceBufferElement>(entity);
            AddComponent(
                entity,
                new PlayerExperience()
                {
                    Experience = 0,
                    Level = 1,
                    NextLevelExperienceRequired = 500,
                }
            );

            if (authoring.IsInvincible)
                AddComponent(entity, new Invincible());
            
        }
    }
}
