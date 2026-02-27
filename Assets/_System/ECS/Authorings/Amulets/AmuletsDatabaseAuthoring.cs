using Unity.Collections;
using UnityEngine;
using Unity.Entities;

public class AmuletsDatabaseAuthoring : MonoBehaviour
{
    public AmuletsDatabaseSO Database;
    private class Baker : Baker<AmuletsDatabaseAuthoring>
    {
        public override void Bake(AmuletsDatabaseAuthoring authoring)
        {
            if (authoring.Database.Amulets == null || authoring.Database.Amulets.Length <= 0)
                return;

            Entity entity = GetEntity(TransformUsageFlags.None);

            var amuletDatas = authoring.Database.Amulets;

            var builder = new BlobBuilder(Allocator.Temp);

            ref AmuletBlobs root = ref builder.ConstructRoot<AmuletBlobs>();

            int count = amuletDatas.Length;
            BlobBuilderArray<AmuletBlob> amuletBlobArrayBuilder = builder.Allocate(ref root.Amulets, count);
            
            for (int i = 0; i < count; i++)
            {
                AmuletSO amuletSO = amuletDatas[i];
                ref AmuletBlob amuletBlob = ref amuletBlobArrayBuilder[i];

                builder.AllocateString(ref amuletBlob.DisplayName, amuletSO.DisplayName);
                builder.AllocateString(ref amuletBlob.Description, amuletSO.Description);

                if (amuletSO.Modifiers != null && amuletSO.Modifiers.Length > 0)
                {
                    int modifersCount = amuletSO.Modifiers.Length;
                    BlobBuilderArray<AmuletModifierBlob> modBuilder = builder.Allocate(ref amuletBlob.Modifiers, modifersCount);
                   
                    for (int j = 0; j < modifersCount; j++)
                    {
                        modBuilder[j] = new AmuletModifierBlob
                        {
                            CharacterStat = amuletSO.Modifiers[j].Stat,
                            ModifierStrategy = amuletSO.Modifiers[j].Strategy,
                            Value = amuletSO.Modifiers[j].Value
                        };
                    }
                }
                else
                {
                    builder.Allocate(ref amuletBlob.Modifiers, 0);
                }
            }

            BlobAssetReference<AmuletBlobs> amuletsDatabaseBlob =
                builder.CreateBlobAssetReference<AmuletBlobs>(Allocator.Persistent);

            AddComponent(entity, new AmuletsDatabase
            {
                Blobs = amuletsDatabaseBlob
            });

            AddBlobAsset(ref amuletsDatabaseBlob, out var hash);

            builder.Dispose();
        }
    }
}