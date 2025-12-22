using Unity.Entities;
using UnityEngine;

public class CharactersDatabaseAuthoring : MonoBehaviour
{
    public CharactersDatabaseSO CharactersDatabase;

    private class Baker : Baker<CharactersDatabaseAuthoring>
    {
        public override void Bake(CharactersDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            if (authoring.CharactersDatabase == null)
            {
                Debug.LogError($"CharactersDatabase is missing!", authoring);
                return;
            }

            AddComponent<CharactersDatabase>(entity);
            var prefabsBuffer = AddBuffer<CharacterPrefabBufferElement>(entity);

            for (int i = 0; i < authoring.CharactersDatabase.Characters.Length ; i++)
            {
                var character = authoring.CharactersDatabase.Characters[i];

                if (character == null)
                {
                    Debug.LogError($"Character prefab for character: {character.name} is missing in database asset!", authoring);
                    continue;
                }
                var characterPrefabEntity = GetEntity(character.GamePrefab, TransformUsageFlags.Dynamic);

                prefabsBuffer.Add(new CharacterPrefabBufferElement()
                {
                    CharacterIndex = i,
                    CharacterPrefabEntity = characterPrefabEntity,
                });
            }
        }
    }
}
