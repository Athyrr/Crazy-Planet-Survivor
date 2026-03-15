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
                var characterData = authoring.CharactersDatabase.Characters[i];

                if (characterData == null)
                {
                    Debug.LogError($"Character prefab for character: {characterData.name} is missing in database asset!", authoring);
                    continue;
                }
                var characterPrefabEntity = GetEntity(characterData.GamePrefab, TransformUsageFlags.Dynamic);

                prefabsBuffer.Add(new CharacterPrefabBufferElement()
                {
                    CharacterIndex = i,
                    CharacterPrefabEntity = characterPrefabEntity,
                });
            }
        }
    }
}
