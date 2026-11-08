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
            var summonPrefabBuffer = AddBuffer<SummonPrefab>(dbEntity);

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

            // Summon transmission (second pass, below): allowed capabilities per DB index and attack indices per Summon spell.
            var allowedByIndex = new ESpellCapability[count];
            var attackIndicesBySummon = new Dictionary<int, List<int>>();

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
                // The Summon bit is derived from the delivery family (SpellSO.Summon), never hand-authored:
                // FireOrbs / FireOrb carried it by hand and lose it here.
                spellBlob.Tag = (spellSO.Tags & ~ESpellTag.Summon) | (spellSO.Summon != null ? ESpellTag.Summon : ESpellTag.None);

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
                allowedByIndex[i] = spellBlob.AllowedCapabilities;

                // Summon delivery family (spec allegeance-summon §2).
                spellBlob.IsSummon = spellSO.Summon != null;
                var attackIndices = new List<int>();
                if (spellBlob.IsSummon)
                {
                    BakeSummon(spellSO, ref spellBlob.Summon, indexBySO, attackIndices);
                    attackIndicesBySummon.Add(i, attackIndices);
                }
                var attackArray = builder.Allocate(ref spellBlob.Summon.AttackSpellIndices, attackIndices.Count);
                for (int k = 0; k < attackIndices.Count; k++)
                    attackArray[k] = attackIndices[k];

                if (spellSO.SpellPrefab != null)
                {
                    var mainEntity = GetEntity(spellSO.SpellPrefab, TransformUsageFlags.Dynamic);
                    mainPrefabBuffer.Add(new SpellPrefab { Prefab = mainEntity });
                }
                else
                {
                    mainPrefabBuffer.Add(new SpellPrefab { Prefab = Entity.Null });
                }

                summonPrefabBuffer.Add(new SummonPrefab
                {
                    Prefab = spellSO.Summon != null && spellSO.Summon.AgentPrefab != null
                        ? GetEntity(spellSO.Summon.AgentPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                });
            }

            // Transmission (spec §2): a Summon spell allows what its attack spells allow, so the pool gating
            // (HasAnySpellAllowing / SpellHasActiveCapability) works unchanged. Second pass: an attack spell may come later.
            foreach (var pair in attackIndicesBySummon)
            {
                ESpellCapability union = allowedByIndex[pair.Key];
                foreach (int attackIndex in pair.Value)
                    union |= allowedByIndex[attackIndex];
                arrayBuilder[pair.Key].AllowedCapabilities = union;
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

        private void BakeSummon(SpellSO spellSO, ref SummonBlob blob, Dictionary<SpellSO, int> indexBySO, List<int> attackIndices)
        {
            SummonSO summon = spellSO.Summon;
            DependsOn(summon);
            if (summon.AgentPrefab != null)
                DependsOn(summon.AgentPrefab);

            var errors = new List<string>();
            if (spellSO.SpellPrefab != null)
                errors.Add($"{spellSO.name}: a Summon spell must have no SpellPrefab (the agent comes from {summon.name}).");
            if (spellSO.BaseCooldown > 0f)
                errors.Add($"{spellSO.name}: a Summon spell must be passive (BaseCooldown <= 0).");
            if (spellSO.BaseAmount < 1)
                errors.Add($"{spellSO.name}: a Summon spell needs BaseAmount >= 1 (FinalAmount = max(1, …) would swallow the first +1).");
            if (summon.AgentPrefab == null)
                errors.Add($"{summon.name}: AgentPrefab is missing.");
            foreach (var attack in summon.AttackSpells)
            {
                if (attack == null)
                    continue;
                if (attack.Summon != null)
                {
                    errors.Add($"{summon.name}: attack spell '{attack.name}' is itself a Summon (ignored).");
                    continue;
                }
                if (!indexBySO.TryGetValue(attack, out int attackIndex))
                {
                    errors.Add($"{summon.name}: attack spell '{attack.name}' is not in the spells database (add it).");
                    continue;
                }
                attackIndices.Add(attackIndex);
            }
            foreach (var err in errors)
                Debug.LogError($"[SpellsDatabaseAuthoring] {err}", spellSO);

            blob.Mobility = summon.Mobility;
            blob.Offense = summon.Offense;
            blob.Targeting = summon.Targeting;
            blob.EndOfLife = summon.EndOfLife;
            blob.TakesAggro = summon.TakesAggro;
            blob.BaseStats = summon.BuildBaseStats();
            blob.Ratios = summon.BuildRatios();
            blob.RingMinRadius = summon.RingMinRadius;
            blob.RingMaxRadius = summon.RingMaxRadius;
            blob.LeashRadius = summon.LeashRadius;
            blob.Acceleration = summon.Acceleration;
            blob.MaxTurnRateDeg = summon.MaxTurnRateDeg;
        }
    }
}
