using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Read-only lookups the per-instance spawn needs (same model as ResolveHitContext).</summary>
public struct SpellSpawnLookups
{
    [ReadOnly] public ComponentLookup<LocalTransform> Transform;
    [ReadOnly] public ComponentLookup<Lifetime> Lifetime;
    [ReadOnly] public ComponentLookup<LinearMovement> LinearMovement;
    [ReadOnly] public ComponentLookup<FollowTargetMovement> FollowMovement;
    [ReadOnly] public ComponentLookup<CopyEntityPosition> CopyPosition;
    [ReadOnly] public ComponentLookup<SelfRotate> SelfRotate;
    [ReadOnly] public ComponentLookup<DamageOnContact> DamageOnContact;
    [ReadOnly] public ComponentLookup<AreaAttack> AreaAttack;
    [ReadOnly] public ComponentLookup<Bounce> Bounce;
    [ReadOnly] public ComponentLookup<Pierce> Pierce;
    [ReadOnly] public ComponentLookup<SpellSource> SpellSource;

    public static SpellSpawnLookups Create(ref SystemState state) => new SpellSpawnLookups
    {
        Transform = state.GetComponentLookup<LocalTransform>(true),
        Lifetime = state.GetComponentLookup<Lifetime>(true),
        LinearMovement = state.GetComponentLookup<LinearMovement>(true),
        FollowMovement = state.GetComponentLookup<FollowTargetMovement>(true),
        CopyPosition = state.GetComponentLookup<CopyEntityPosition>(true),
        SelfRotate = state.GetComponentLookup<SelfRotate>(true),
        DamageOnContact = state.GetComponentLookup<DamageOnContact>(true),
        AreaAttack = state.GetComponentLookup<AreaAttack>(true),
        Bounce = state.GetComponentLookup<Bounce>(true),
        Pierce = state.GetComponentLookup<Pierce>(true),
        SpellSource = state.GetComponentLookup<SpellSource>(true),
    };

    public void Update(ref SystemState state)
    {
        Transform.Update(ref state); Lifetime.Update(ref state); LinearMovement.Update(ref state);
        FollowMovement.Update(ref state); CopyPosition.Update(ref state); SelfRotate.Update(ref state);
        DamageOnContact.Update(ref state); AreaAttack.Update(ref state); Bounce.Update(ref state);
        Pierce.Update(ref state); SpellSource.Update(ref state);
    }
}

/// <summary>One fully-resolved spell instance to spawn (the caller decided position, power and form).</summary>
public struct SpellSpawnParams
{
    public Entity Prefab;
    public Entity Caster;
    public int DatabaseIndex;          // ROOT spell, written to SpellSource
    public float3 Position;
    public quaternion Rotation;
    public float3 Direction;
    public Entity Target;              // FollowTargetMovement target (Null = leave the prefab value)
    public float3 CasterOffset;        // CopyEntityPosition offset (caster-local)
    public float Scale;                // × prefab scale, × area radii, = DamageOnContact.AreaRadius
    public float Damage;
    public float Speed;
    public float Duration;             // Lifetime, if the prefab has one
    public float TickRate;             // OverTime areas
    public float CritChance;
    public float CritMultiplier;
    public ESpellTag Tags;
    public FixedList32Bytes<EffectSpec> Effects;
    public uint TargetLayers;
    public ESpellCapability Allowed;
    public int Bounces;
    public float BounceRange;
    public int Pierces;
}

public static class SpellSpawnUtils
{
    public static float PrefabScale(in ComponentLookup<LocalTransform> transformLookup, Entity prefab)
        => transformLookup.HasComponent(prefab) ? transformLookup[prefab].Scale : 1f;

    /// <summary>The per-instance steps formerly inlined in SpellCastingSystem's spawn loop, in the same order.</summary>
    public static Entity SpawnSpellInstance(in SpellSpawnLookups lk, EntityCommandBuffer.ParallelWriter ecb, int sortKey, in SpellSpawnParams p)
    {
        var e = ecb.Instantiate(sortKey, p.Prefab);

        if (lk.SpellSource.HasComponent(p.Prefab))
            ecb.SetComponent(sortKey, e, new SpellSource { CasterEntity = p.Caster, DatabaseIndex = p.DatabaseIndex });

        ecb.SetComponent(sortKey, e, new LocalTransform
        {
            Position = p.Position,
            Rotation = p.Rotation,
            Scale = PrefabScale(in lk.Transform, p.Prefab) * p.Scale,
        });

        if (lk.LinearMovement.HasComponent(p.Prefab))
            ecb.SetComponent(sortKey, e, new LinearMovement { Direction = p.Direction, Speed = p.Speed });

        if (lk.FollowMovement.HasComponent(p.Prefab) && p.Target != Entity.Null)
            ecb.SetComponent(sortKey, e, new FollowTargetMovement { Target = p.Target, Speed = p.Speed });

        if (lk.CopyPosition.HasComponent(p.Prefab))
        {
            var copyPos = lk.CopyPosition[p.Prefab];
            copyPos.Target = p.Caster;
            copyPos.Offset = p.CasterOffset;
            ecb.SetComponent(sortKey, e, copyPos);
        }

        if (lk.DamageOnContact.HasComponent(p.Prefab))
        {
            ecb.SetComponent(sortKey, e, new DamageOnContact
            {
                Damage = p.Damage,
                Tags = p.Tags,
                EffectsToApply = p.Effects,
                AreaRadius = p.Scale,
                TotalCritChance = p.CritChance,
                TotalCritMultiplier = p.CritMultiplier,
                TargetLayers = p.TargetLayers,
            });
        }

        if (lk.AreaAttack.HasComponent(p.Prefab))
        {
            var zone = lk.AreaAttack[p.Prefab]; // shape / cadence / timing from the prefab
            SetZonePower(ref zone, p.Damage, p.CritChance, p.CritMultiplier, p.Caster, p.TargetLayers, p.Tags, in p.Effects);

            if (zone.Cadence == EZoneCadence.OverTime)
            {
                float baseRadius = zone.PrefabRadius > 0f ? zone.PrefabRadius : 1f;
                zone.PrefabRadius = baseRadius;
                zone.RadiusStart = p.Scale * baseRadius;
                zone.RadiusEnd = zone.RadiusStart;
                zone.TickRate = p.TickRate;
                ecb.AddBuffer<TickDamageTarget>(sortKey, e);
            }
            else // Burst
            {
                ScaleBurstRadii(ref zone, p.Scale);
            }

            ecb.SetComponent(sortKey, e, zone);
        }

        if (lk.Lifetime.HasComponent(p.Prefab))
            ecb.SetComponent(sortKey, e, new Lifetime { Duration = p.Duration, TimeLeft = p.Duration });

        if (lk.SelfRotate.HasComponent(p.Prefab))
            ecb.SetComponent(sortKey, e, new SelfRotate { RotationSpeed = p.Speed });

        // Bounce / Pierce: counter > 0 AND allowed AND slot present (the tag describes, it does not create).
        if (p.Bounces > 0 && (p.Allowed & ESpellCapability.Bounce) != 0 && lk.Bounce.HasComponent(p.Prefab))
        {
            ecb.SetComponentEnabled<Bounce>(sortKey, e, true);
            ecb.SetComponent(sortKey, e, new Bounce { RemainingBounces = p.Bounces, BounceRange = p.BounceRange, BounceSpeed = p.Speed });
        }

        if (p.Pierces > 0 && (p.Allowed & ESpellCapability.Pierce) != 0 && lk.Pierce.HasComponent(p.Prefab))
        {
            ecb.SetComponentEnabled<Pierce>(sortKey, e, true);
            ecb.SetComponent(sortKey, e, new Pierce { RemainingPierces = p.Pierces });
        }

        return e;
    }

    /// <summary>Low-level "pose an AreaAttack Burst" with no parent spell assumed: enemy ExplodeOnDeath,
    /// future environment explosions, and emitted explosions. caster = Null + databaseIndex = -1 → no tracking,
    /// no life steal (SpellSource is overwritten with -1 if the prefab carries one).</summary>
    public static Entity SpawnBurstArea(in ComponentLookup<LocalTransform> transformLookup, in ComponentLookup<AreaAttack> areaLookup,
        in ComponentLookup<SpellSource> sourceLookup, EntityCommandBuffer.ParallelWriter ecb, int sortKey, Entity prefab,
        float3 position, quaternion rotation, float scale, float damage, float critChance, float critMultiplier,
        ESpellTag tags, uint targetLayers, Entity caster, int databaseIndex, in FixedList32Bytes<EffectSpec> effects)
    {
        var e = ecb.Instantiate(sortKey, prefab);
        float finalScale = PrefabScale(in transformLookup, prefab) * scale;

        ecb.SetComponent(sortKey, e, new LocalTransform { Position = position, Rotation = rotation, Scale = finalScale });
        ecb.SetComponent(sortKey, e, new LocalToWorld { Value = float4x4.TRS(position, rotation, new float3(finalScale)) });

        if (sourceLookup.HasComponent(prefab))
            ecb.SetComponent(sortKey, e, new SpellSource { CasterEntity = caster, DatabaseIndex = databaseIndex });

        if (areaLookup.HasComponent(prefab))
        {
            var zone = areaLookup[prefab];
            ScaleBurstRadii(ref zone, scale);
            SetZonePower(ref zone, damage, critChance, critMultiplier, caster, targetLayers, tags, in effects);
            ecb.SetComponent(sortKey, e, zone);
        }

        return e;
    }

    // Shared zone core: the single place that writes a zone's power/ownership and scales Burst radii,
    // used by both SpawnSpellInstance and SpawnBurstArea so the two never diverge.

    /// <summary>Power and ownership of a freshly-spawned zone; restarts its clock.</summary>
    private static void SetZonePower(ref AreaAttack zone, float damage, float critChance, float critMultiplier,
        Entity caster, uint targetLayers, ESpellTag tags, in FixedList32Bytes<EffectSpec> effects)
    {
        zone.Damage = damage;
        zone.CritChance = critChance;
        zone.CritMultiplier = critMultiplier;
        zone.Caster = caster;
        zone.TargetLayers = targetLayers;
        zone.Tags = tags;
        zone.EffectsToApply = effects;
        zone.ElapsedTime = 0f;
    }

    /// <summary>Burst radii scale once at spawn (the prefab's start/end radii are the unit-size wave).</summary>
    private static void ScaleBurstRadii(ref AreaAttack zone, float scale)
    {
        zone.RadiusStart *= scale;
        zone.RadiusEnd *= scale;
    }
}
