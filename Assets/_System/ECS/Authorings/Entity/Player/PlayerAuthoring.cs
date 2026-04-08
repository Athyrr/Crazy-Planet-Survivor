using System;
using System.Collections.Generic;
using _System.ECS.Authorings.Ressources;
using _System.ECS.Components.Entity;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(DestructibleAuthoring))]
public class PlayerAuthoring : MonoBehaviour
{
    private GameUpgradesConfigSO _gameUpgradesConfig;

    [Header("Datas")] [Tooltip("The character's base stats and initial spells.")]
    public CharacterSO characterData;

    [Header("Movement Settings")]
    [Tooltip("If true, the player will be snapped perfectly on the ground following the terrain height.")]
    public bool UseSnappedMovement = true;

    [Header("Debug")] [Tooltip("If true, the player will be invincible.")]
    public bool IsInvincible = false;

    private void OnValidate()
    {
        if (_gameUpgradesConfig == null)
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:GameUpgradesConfigSO");

            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                _gameUpgradesConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<GameUpgradesConfigSO>(path);

                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(this);
                }
            }
            else
            {
                Debug.LogWarning("[PlayerAuthoring] 'GlobalUpgradesConfigSO' not found in project!");
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
                Debug.LogError($"PlayerAuthoring on {authoring.name} is missing CharacterDataSO!", authoring);
                return;
            }

            if (authoring._gameUpgradesConfig == null)
            {
                Debug.LogError($"[PlayerAuthoring] '{authoring.name}' needs a reference to GameUpgradesConfigSO.",
                    authoring);
                return;
            }

            List<UpgradeSO> gameUpgradesList = authoring._gameUpgradesConfig.GetFlattenedUpgrades();
            var data = authoring.characterData;
            var baseStats = data.coreStats;
            var initialSpells = data.InitialSpells;

            AddComponent(entity, new Player());
            AddComponent(entity, new InputData { Value = new float2(0, 0) });
            AddComponent(entity, new Health { Value = (int)baseStats.MaxHealth });

            AddComponent(entity, new LinearMovement { Direction = float3.zero, Speed = baseStats.BaseMoveSpeed });
            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddBuffer<DamageBufferElement>(entity);

            AddComponent(entity, new CoreStats
            {
                // Bases
                BaseArmor = baseStats.BaseArmor,
                BaseMoveSpeed = baseStats.BaseMoveSpeed,
                BasePickupRange = baseStats.BasePickupRange,

                MaxHealth = baseStats.MaxHealth,
                HealthRecovery = baseStats.HealthRecovery,
                DamageReductionMultiplier = baseStats.DamageReductionMultiplier,
                MoveSpeedMultiplier = baseStats.MoveSpeedMultiplier,
                PickupRangeMultiplier = baseStats.PickupRangeMultiplier,

                GlobalDamageMultiplier = baseStats.GlobalDamageMultiplier,
                GlobalCooldownReductionMultiplier = baseStats.GlobalCooldownReductionMultiplier,
                GlobalSpellAreaMultiplier = baseStats.GlobalSpellAreaMultiplier,
                GlobalSpellSizeMultiplier = baseStats.GlobalSpellSizeMultiplier,
                GlobalSpellSpeedMultiplier = baseStats.GlobalSpellSpeedMultiplier,
                GlobalSpellDurationMultiplier = baseStats.GlobalSpellDurationMultiplier,
                GlobalCastRangeMultiplier = baseStats.GlobalCastRangeMultiplier,

                GlobalAmountBonus = baseStats.GlobalAmountBonus,
                GlobalPierceBonus = baseStats.GlobalPierceBonus,
                GlobalBounceBonus = baseStats.GlobalBounceBonus,

                CritChance = baseStats.CritChance,
                CritDamageMultiplier = baseStats.CritDamageMultiplier,
            });

            // Spells buffer
            AddBuffer<SpellModifier>(entity);

            // Spells
            AddBuffer<ActiveSpell>(entity);
            DynamicBuffer<SpellActivationRequest> spellActivationBuffer = AddBuffer<SpellActivationRequest>(entity);

            var baseSpellsBuffer = AddBuffer<BaseSpell>(entity);

            if (initialSpells != null)
            {
                foreach (var spellSO in initialSpells)
                {
                    if (spellSO == null || spellSO.SpellPrefab == null)
                        continue;

                    spellActivationBuffer.Add(new SpellActivationRequest { ID = spellSO.ID });

                    // todo remove if not used and destroy script
                    baseSpellsBuffer.Add(new BaseSpell { ID = spellSO.ID });
                }
            }

            // Upgrades Pool
            DynamicBuffer<StatsUpgradePoolBufferElement> statsUpgradebuffer =
                AddBuffer<StatsUpgradePoolBufferElement>(entity);
            if (data.StatsUpgradesPool != null)
            {
                foreach (var localUpgrade in data.StatsUpgradesPool.Upgrades)
                {
                    if (localUpgrade == null)
                        continue;
                    int globalIndex = gameUpgradesList.IndexOf(localUpgrade);
                    if (globalIndex != -1)
                        statsUpgradebuffer.Add(new StatsUpgradePoolBufferElement { DatabaseIndex = globalIndex });
                    else
                        Debug.LogWarning(
                            $"Upgrade '{localUpgrade.name}' is in CharacterData but NOT in the Global Database Authoring.",
                            authoring);
                }
            }

            DynamicBuffer<SpellsUpgradePoolBufferElement> spellsUpgradebuffer =
                AddBuffer<SpellsUpgradePoolBufferElement>(entity);
            if (data.SpellUpgradesPool != null)
            {
                foreach (var localUpgrade in data.SpellUpgradesPool.Upgrades)
                {
                    if (localUpgrade == null)
                        continue;
                    int globalIndex = gameUpgradesList.IndexOf(localUpgrade);
                    if (globalIndex != -1)
                        spellsUpgradebuffer.Add(new SpellsUpgradePoolBufferElement { DatabaseIndex = globalIndex });
                    else
                        Debug.LogWarning(
                            $"Upgrade '{localUpgrade.name}' is in CharacterData but NOT in the Global Database.",
                            authoring);
                }
            }

            // Experience / Ressources 
            AddBuffer<CollectedExperienceBufferElement>(entity);
            AddComponent(entity, new PlayerExperience
            {
                Experience = 0,
                Level = 1,
                NextLevelExperienceRequired = 500,
            });

            AddBuffer<CollectedRessourcesBufferElement>(entity);
            var resséourcesMap = new FixedList128Bytes<int>();

            foreach (ERessourceType type in Enum.GetValues(typeof(ERessourceType)))
                resséourcesMap.Add(0);

            AddComponent(entity, new PlayerRessources
            {
                Ressources = resséourcesMap
            });

            // Invincibility 
            if (authoring.IsInvincible)
            {
                AddComponent(entity, new Invincible());
                // SetComponentEnabled<Destructible>(entity, false);
            }
        }
    }
}