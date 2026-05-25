using _System.ECS.Authorings.Resources;
using Unity.Entities;

/// <summary>
/// Extension methods for working with ResourceBufferElements and resource cost for shop operations.
/// </summary>
public static class ResourceHelper
{
    /// <summary>
    /// Adds or deducts an amount of a resource type in the buffer.
    /// Creates a new entry if the type doesn't exist yet.
    /// </summary>
    public static void AddOrDeduct(this DynamicBuffer<ResourceBufferElement> buffer, EResourceType type, int amount)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Type == type)
            {
                buffer[i] = new ResourceBufferElement
                {
                    Type = type,
                    Value = buffer[i].Value + amount
                };
                return;
            }
        }

        buffer.Add(new ResourceBufferElement { Type = type, Value = amount });
    }

    /// <summary>
    /// Returns the current amount of a resource type, or 0 if not found.
    /// </summary>
    public static int GetAmount(this DynamicBuffer<ResourceBufferElement> buffer, EResourceType type)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Type == type)
                return buffer[i].Value;
        }

        return 0;
    }

    /// <summary>
    /// Checks whether the buffer has enough of each cost defined in the array.
    /// </summary>
    public static bool HasEnough(this DynamicBuffer<ResourceBufferElement> buffer, ResourceCost[] costs)
    {
        if (costs == null) return true;

        foreach (var cost in costs)
        {
            if (cost.Amount <= 0) continue;

            if (buffer.GetAmount(cost.Type) < cost.Amount)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Deducts all costs from the resources buffer. Does NOT check if enough — call HasEnough first.
    /// </summary>
    public static void DeductCost(this DynamicBuffer<ResourceBufferElement> buffer, ResourceCost[] costs)
    {
        if (costs == null) return;

        foreach (var cost in costs)
        {
            if (cost.Amount <= 0) continue;
            buffer.AddOrDeduct(cost.Type, -cost.Amount);
        }
    }

    /// <summary>
    /// Syncs the meta resources buffer back to the Save file and persists to disk.
    /// Call this after modifying meta resources in a shop.
    /// </summary>
    public static void Save(this DynamicBuffer<ResourceBufferElement> metaResources)
    {
        var save = SaveManager.GetCurrentSaveAs<Save>();
        if (save == null) return;

        for (int i = 0; i < save.ressources.Ressources.Length; i++)
        {
            save.ressources.Ressources[i] = metaResources.GetAmount((EResourceType)i);
        }

        SaveManager.ManualSave();
    }
}