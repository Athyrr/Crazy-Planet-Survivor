using Unity.Entities;

namespace _System.ECS.Components.Audio
{
    public struct SoundPlayerTag : IComponentData
    {
        public int GemsCollectedSound;
        public bool HaveBossSpawnedSound;
    }
}
