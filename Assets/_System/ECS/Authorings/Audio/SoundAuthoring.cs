using _System.ECS.Components.Audio;
using Unity.Entities;
using UnityEngine;

class SoundAuthoring : MonoBehaviour { }

class SoundAuthoringBaker : Baker<SoundAuthoring>
{
    public override void Bake(SoundAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(
            entity,
            new SoundPlayerTag { GemsCollectedSound = 0, HaveBossSpawnedSound = false }
        );
    }
}
