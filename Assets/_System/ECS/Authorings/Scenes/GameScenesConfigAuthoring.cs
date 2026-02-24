using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using UnityEditor;

public class GameScenesConfigAuthoring : MonoBehaviour
{
    [System.Serializable]
    public struct PlanetScene
    {
        public EntitySceneReference SceneReference;
        public EPlanetID PlanetID;
    }

    [Header("Planets scenes")] 
    public PlanetScene[] PlanetScenes;

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
                if (scene.SceneReference.Equals(default(EntitySceneReference)))
                    continue;

                buffer.Add(new PlanetSceneRefBufferElement()
                {
                    PlanetID = scene.PlanetID,
                    SceneReference = scene.SceneReference
                });
            }
        }
    }
}