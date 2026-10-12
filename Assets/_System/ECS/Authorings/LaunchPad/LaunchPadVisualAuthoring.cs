using Unity.Entities;
using UnityEngine;

/// <summary>
/// Put on a child of a launch pad (e.g. the floating star) to make it spin and bob in place.
/// </summary>
public class LaunchPadVisualAuthoring : MonoBehaviour
{
    [Tooltip("Spin speed around the local up axis (degrees per second).")]
    [SerializeField] private float _spinDegreesPerSecond = 90f;

    [Tooltip("Vertical bob amplitude (local units).")]
    [SerializeField] private float _bobAmplitude = 0.25f;

    [Tooltip("Bob cycles per second.")]
    [SerializeField] private float _bobFrequency = 0.6f;

    private class Baker : Baker<LaunchPadVisualAuthoring>
    {
        public override void Bake(LaunchPadVisualAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            var t = authoring.transform;

            AddComponent(entity, new LaunchPadVisual
            {
                SpinSpeed = authoring._spinDegreesPerSecond * Mathf.Deg2Rad,
                BobAmplitude = authoring._bobAmplitude,
                BobFrequency = authoring._bobFrequency,
                BasePosition = t.localPosition,
                BaseRotation = t.localRotation,
            });
        }
    }
}
