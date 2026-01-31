using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

public class GameScenesConfigAuthoring : MonoBehaviour
{
    [Header("Planets scenes")]
    public PlanetScene[] PlanetScenes;

    [System.Serializable]
    public struct PlanetScene
    {
        public EPlanetID ID;
        public UnityEditor.SceneAsset Scene;
    }

    private class Baker : Baker<GameScenesConfigAuthoring>
    {
        public override void Bake(GameScenesConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            if (authoring.PlanetScenes == null)
                return;

            var buffer = AddBuffer<PlanetSceneRefBufferElement>(entity);

            foreach (var scene in authoring.PlanetScenes)
            {
                if (scene.Scene == null)
                    continue;

                buffer.Add(new PlanetSceneRefBufferElement()
                {
                    PlanetID = scene.ID,
                    SceneReference = new EntitySceneReference(scene.Scene)
                });
            }
        }
    }
}
