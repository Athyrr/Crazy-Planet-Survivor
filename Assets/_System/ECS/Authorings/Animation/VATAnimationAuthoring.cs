using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Plays a baked VAT atlas on this renderer, blended by the entity's measured speed (VATAnimationSystem).
/// Goes on the GameObject of the Visual's MeshRenderer, whose mesh must be the mesh asset that was baked.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class VATAnimationAuthoring : MonoBehaviour
{
    [Tooltip("Baked VAT atlas (VATAnimationData_<Model>) played on this renderer.")]
    public VATAnimationSO Animation;

    private class Baker : Baker<VATAnimationAuthoring>
    {
        public override void Bake(VATAnimationAuthoring authoring)
        {
            var animation = authoring.Animation;
            if (animation == null)
            {
                Debug.LogError($"[VATAnimationAuthoring] {authoring.name}: no Animation assigned.", authoring);
                return;
            }
            DependsOn(animation);

            if (animation.Clips == null || animation.Clips.Length == 0)
            {
                Debug.LogError($"[VATAnimationAuthoring] {authoring.name}: '{animation.name}' has no clip (bake it first).", authoring);
                return;
            }
            if (VATClipTableBuilder.HasDuplicateNativeSpeeds(animation))
            {
                Debug.LogError($"[VATAnimationAuthoring] {authoring.name}: two clips of '{animation.name}' share a NativeSpeed; give each a distinct value.", authoring);
                return;
            }

            var meshFilter = GetComponent<MeshFilter>();
            var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            DependsOn(mesh);
            if (mesh == null || mesh.vertexCount != animation.VertexCount)
            {
                Debug.LogError($"[VATAnimationAuthoring] {authoring.name}: mesh has {(mesh == null ? 0 : mesh.vertexCount)} vertices, '{animation.name}' was baked for {animation.VertexCount} (SV_VertexID needs the baked mesh asset).", authoring);
                return;
            }

            var entity = GetEntity(TransformUsageFlags.Renderable);
            var blob = VATClipTableBuilder.Build(animation, Allocator.Persistent);
            AddBlobAsset(ref blob, out _);
            AddComponent(entity, new VATClipTable { Value = blob });
            AddComponent(entity, new VATAnimationState());
            float firstRow = blob.Value.Clips[0].StartRow;
            AddComponent(entity, new VATFrame { Value = new float4(firstRow, firstRow, 0f, 0f) });
        }
    }
}
