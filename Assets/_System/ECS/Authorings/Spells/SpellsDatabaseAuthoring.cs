using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class SpellsDatabaseAuthoring : MonoBehaviour
{
    public SpellDatabaseSO GameSpellDatabase;

    private class Baker : Baker<SpellsDatabaseAuthoring>
    {
        public override void Bake(SpellsDatabaseAuthoring authoring)
        {
            Entity dbEntity = GetEntity(TransformUsageFlags.None);

            var mainPrefabBuffer = AddBuffer<SpellPrefab>(dbEntity);

            // Create spell blobs for data
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SpellBlobs>();

            int count = authoring.GameSpellDatabase.Spells.Length;
            BlobBuilderArray<SpellBlob> arrayBuilder = builder.Allocate(ref root.Spells, count);

            // Emitter child resolution: first occurrence of each non-null SpellSO = its blob index.
            var indexBySO = new Dictionary<SpellSO, int>();
            for (int i = 0; i < count; i++)
            {
                SpellSO so = authoring.GameSpellDatabase.Spells[i];
                if (so != null && !indexBySO.ContainsKey(so))
                    indexBySO.Add(so, i);
            }

            for (int i = 0; i < count; i++)
            {
                SpellSO spellSO = authoring.GameSpellDatabase.Spells[i];

                if (spellSO == null)
                    continue;

                DependsOn(spellSO);

                ref var spellBlob = ref arrayBuilder[i];

                // Identity
                spellBlob.ID = spellSO.ID;
                // spellBlob.DisplayName = spellSO.DisplayName;
                spellBlob.Tag = spellSO.Tags;

                // Ruling D3: permission (SpellSO) AND real slot on the prefab. A spell that allows Bounce but whose prefab
                // has no BounceAuthoring cannot bounce, so it must not count for the upgrade filter either.
                ESpellCapability slots = ESpellCapability.None;
                if (spellSO.SpellPrefab != null)
                {
                    DependsOn(spellSO.SpellPrefab); // rebake when the prefab gains/loses a slot
                    if (spellSO.SpellPrefab.GetComponent<BounceAuthoring>() != null) slots |= ESpellCapability.Bounce;
                    if (spellSO.SpellPrefab.GetComponent<PierceAuthoring>() != null) slots |= ESpellCapability.Pierce;
                }

                // Base Stats
                spellBlob.BaseDamage = spellSO.BaseDamage;
                spellBlob.BaseCooldown = spellSO.BaseCooldown;
                spellBlob.BaseSpeed = spellSO.BaseSpeed;
                spellBlob.Lifetime = spellSO.Lifetime;
                spellBlob.BaseCastRange = spellSO.BaseCastRange;
                spellBlob.SizeScalesRange = spellSO.SizeScalesRange;
                spellBlob.BaseSpawnOffset = spellSO.BaseSpawnOffset;
                spellBlob.BaseSize = spellSO.BaseSize;

                spellBlob.BaseAmount = spellSO.BaseAmount;

                // Multi-cast (how Amount is consumed at cast time)
                spellBlob.MultiCast = spellSO.MultiCast;
                spellBlob.SpreadAngleDegrees = spellSO.SpreadAngleDegrees;
                spellBlob.MaxSpreadDegrees = spellSO.MaxSpreadDegrees;

                // Targeting
                spellBlob.TargetingMode = spellSO.TargetingMode;

                // Mechanics
                spellBlob.Bounces = spellSO.Bounces;
                spellBlob.BounceRange = spellSO.BounceRange;
                spellBlob.Pierces = spellSO.Pierces;

                // Tick effects
                spellBlob.TickRate = spellSO.TickRate;

                var effectsArray = builder.Allocate(ref spellBlob.Effects, spellSO.Effects?.Length ?? 0);
                for (int e = 0; e < effectsArray.Length; e++)
                    effectsArray[e] = spellSO.Effects[e];

                // Emitters: validated entries only, child resolved to its DB index. Invalid entry = LogError + skipped.
                var emitterErrors = new List<string>();
                bool[] emitterValid = SpellEmitterValidation.Validate(spellSO, emitterErrors);
                var kept = new List<(SpellEmitterEntry entry, int childIndex)>();
                for (int k = 0; k < emitterValid.Length; k++)
                {
                    if (!emitterValid[k]) continue;
                    var entry = spellSO.Emitters[k];
                    if (!indexBySO.TryGetValue(entry.Child, out int childIndex))
                    {
                        emitterErrors.Add($"{spellSO.name} emitter[{k}]: child '{entry.Child.name}' is not in {authoring.GameSpellDatabase.name} (run Populate).");
                        continue;
                    }
                    DependsOn(entry.Child);
                    kept.Add((entry, childIndex));
                }
                foreach (var err in emitterErrors)
                    Debug.LogError($"[SpellsDatabaseAuthoring] {err}", spellSO);

                ESpellCapability emitterCaps = ESpellCapability.None;
                var emitterArray = builder.Allocate(ref spellBlob.Emitters, kept.Count);
                for (int k = 0; k < kept.Count; k++)
                {
                    var (entry, childIndex) = kept[k];
                    emitterArray[k] = new EmitterBlob
                    {
                        Capability = entry.Capability, Trigger = entry.Trigger, Attachment = entry.Attachment,
                        Layout = entry.Layout, Count = entry.Count, FixedCount = entry.FixedCount,
                        // Decision I1 (channeled-beam): an attached AtPoint child sits ON the caster — no orbit radius
                        // (the attached path ignores Layout and always uses the orbit, SpellEmissionSystem SpawnAttached).
                        ChildIndex = childIndex,
                        Radius = entry.Attachment == EEmitterAttachment.Attached && entry.Layout == EEmitterLayout.AtPoint ? 0f : entry.Radius,
                        SpreadAngleDegrees = entry.SpreadAngleDegrees,
                        Frame = entry.Frame, Chance = entry.Chance, DamageRatio = entry.DamageRatio,
                        LineStartDistance = entry.LineStartDistance, LineSpacing = entry.LineSpacing,
                        Timing = entry.Timing, SequenceInterval = entry.SequenceInterval,
                    };
                    if (entry.Capability == ESpellCapability.Explode) emitterCaps |= ESpellCapability.Explode;
                    if (entry.Capability == ESpellCapability.Split) emitterCaps |= ESpellCapability.Split;
                    if (entry.Capability == ESpellCapability.Emit) emitterCaps |= ESpellCapability.Emit;
                }

                // Ruling D3 (Bounce/Pierce: permission AND slot) + emitter entries (Explode/Split/Emit).
                // SpellSO.AllowedCapabilities stays Bounce|Pierce only: this is the single place Emit becomes allowed.
                spellBlob.AllowedCapabilities = (spellSO.AllowedCapabilities & slots) | emitterCaps;
                spellBlob.BaseGrantedCapabilities = spellSO.BaseGrantedCapabilities;
                spellBlob.BaseSplitCount = spellSO.BaseSplitCount;
                spellBlob.BaseSplitDepth = spellSO.BaseSplitDepth;
                spellBlob.ReHitDelayOverride = spellSO.ReHitDelayOverride;

                if (spellSO.SpellPrefab != null)
                {
                    var mainEntity = GetEntity(spellSO.SpellPrefab, TransformUsageFlags.Dynamic);
                    mainPrefabBuffer.Add(new SpellPrefab { Prefab = mainEntity });
                }
                else
                {
                    mainPrefabBuffer.Add(new SpellPrefab { Prefab = Entity.Null });
                }
            }

            var spellsDatabaseBlob = builder.CreateBlobAssetReference<SpellBlobs>(
                Allocator.Persistent
            );

            AddComponent(dbEntity, new SpellsDatabase { Blobs = spellsDatabaseBlob });

            // Register blob asset (auto free memory)
            AddBlobAsset(ref spellsDatabaseBlob, out var hash);

            // Dispose builder
            builder.Dispose();
        }
    }
}
