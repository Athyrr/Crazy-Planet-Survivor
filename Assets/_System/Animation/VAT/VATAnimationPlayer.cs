using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Loops one clip of a baked VAT atlas on a GameObject renderer, outside ECS (UI previews), with the same
/// VATAnimationMath.Step as VATAnimationSystem; writes _VATFrame through a MaterialPropertyBlock.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class VATAnimationPlayer : MonoBehaviour
{
    [Tooltip("Baked VAT atlas (VATAnimationData_<Model>).")]
    public VATAnimationSO Animation;

    [Tooltip("Index in Animation.Clips of the clip to loop (0 = first baked clip, the idle by convention).")]
    [Min(0)] public int ClipIndex;

    private Renderer _renderer;
    private MaterialPropertyBlock _block;
    private BlobAssetReference<VATClipTableBlob> _table;
    private float _speed;
    private float _phase;

    private void OnEnable()
    {
        if (Animation == null || Animation.Clips == null || Animation.Clips.Length == 0)
        {
            Debug.LogError($"[VATAnimationPlayer] {name}: no baked Animation assigned.", this);
            enabled = false;
            return;
        }
        _renderer = GetComponent<Renderer>();
        _block ??= new MaterialPropertyBlock();
        _table = VATClipTableBuilder.Build(Animation, Allocator.Persistent);
        // Playing a clip = feeding its native speed to the shared math: that clip alone, at its own cadence.
        _speed = Animation.Clips[Mathf.Clamp(ClipIndex, 0, Animation.Clips.Length - 1)].NativeSpeed;
        _phase = UnityEngine.Random.value;
    }

    private void Update()
    {
        if (!_table.IsCreated) return;
        float4 frame = VATAnimationMath.Step(ref _table.Value, _speed, Time.deltaTime, ref _phase);
        _renderer.GetPropertyBlock(_block);
        _block.SetVector(VATShaderProperties.FrameId, new Vector4(frame.x, frame.y, frame.z, frame.w));
        _renderer.SetPropertyBlock(_block);
    }

    private void OnDisable()
    {
        if (_table.IsCreated) _table.Dispose();
    }
}
