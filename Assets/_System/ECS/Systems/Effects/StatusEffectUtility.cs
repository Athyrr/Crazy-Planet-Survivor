using Unity.Entities;
using Unity.Mathematics;

/// <summary>ECS orchestration layer over StatusEffectInstance — read-modify-write against a live buffer, so
/// callers must own that buffer exclusively for the duration of the call (see ActiveEffectsSystem's
/// DrainApplyRequestsJob, the only caller — one entity's buffer per job iteration, never shared).</summary>
public static class StatusEffectUtility
{
    public static void ApplyOrRefresh(ref DynamicBuffer<StatusEffectInstance> buffer, EffectType type,
        Entity source, float magnitude, float duration, float3 direction, EStackMode stackMode, int maxStacks)
    {
        int refreshIndex = -1;
        int sameTypeSourceCount = 0;
        int oldestIndex = -1;
        float oldestRemaining = float.MaxValue;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Type != type || buffer[i].Source != source)
                continue;

            sameTypeSourceCount++;
            if (buffer[i].RemainingTime < oldestRemaining)
            {
                oldestRemaining = buffer[i].RemainingTime;
                oldestIndex = i;
            }

            if (stackMode == EStackMode.RefreshOnly)
            {
                refreshIndex = i;
                break;
            }
        }

        if (stackMode == EStackMode.RefreshOnly && refreshIndex >= 0)
        {
            var inst = buffer[refreshIndex];
            inst.Magnitude = math.max(inst.Magnitude, magnitude);
            inst.RemainingTime = duration;
            inst.Direction = direction;
            buffer[refreshIndex] = inst;
            return;
        }

        if (stackMode == EStackMode.StackCapped && sameTypeSourceCount >= math.max(1, maxStacks))
            buffer.RemoveAt(oldestIndex);

        buffer.Add(new StatusEffectInstance
        {
            Type = type,
            Source = source,
            Magnitude = magnitude,
            RemainingTime = duration,
            Direction = direction,
        });
    }
}
