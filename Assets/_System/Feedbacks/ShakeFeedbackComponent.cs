using _System.Settings;
using Unity.Cinemachine;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class ShakeFeedbackComponent : MonoBehaviour
{
    public CinemachineBasicMultiChannelPerlin CinemachineBasicMultiChannelPerlin;

    private EntityManager _entityManager;
    private EntityQuery _shakeRequestQuery;

    [Header("Fallback (used only when the CameraShakeSettings SO cannot be resolved)")]
    [SerializeField]
    private float _shakeDuration = 0f;

    [SerializeField]
    private float _amplitude = 2f;

    [SerializeField]
    private float _frequency = 0.1f;

    private float _shakeTimer = 0f;

    private void Start()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            Debug.LogError("ECS World not found!");
            this.enabled = false;
            return;
        }

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _shakeRequestQuery = _entityManager.CreateEntityQuery(typeof(ShakeFeedbackRequest));

        if (CinemachineBasicMultiChannelPerlin != null)
        {
            CinemachineBasicMultiChannelPerlin.FrequencyGain = 0f;
            CinemachineBasicMultiChannelPerlin.AmplitudeGain = 0f;
        }
    }

    void Update()
    {
        if (CinemachineBasicMultiChannelPerlin == null)
            return;

        if (_shakeTimer > 0f)
        {
            _shakeTimer -= Time.deltaTime;

            if (_shakeTimer <= 0)
            {
                _shakeTimer = 0;
                CinemachineBasicMultiChannelPerlin.AmplitudeGain = 0f;
                CinemachineBasicMultiChannelPerlin.FrequencyGain = 0f;
            }
            return;
        }

        if (_shakeRequestQuery.IsEmpty)
            return;

        NativeArray<Entity> requestEntities = _shakeRequestQuery.ToEntityArray(Allocator.Temp);

        // Several requests can land in the same frame (e.g. a DoT tick + a boss hit, or the
        // death-shake). Keep the strongest source so the heaviest shake wins.
        EDamageShakeSource strongest = EDamageShakeSource.None;
        for (int i = 0; i < requestEntities.Length; i++)
        {
            var request = _entityManager.GetComponentData<ShakeFeedbackRequest>(requestEntities[i]);
            if ((byte)request.Source >= (byte)strongest)
                strongest = request.Source;
        }

        float amplitude, frequency, duration;
        var settings = CpCameraShakeSettings.I;
        if (settings != null)
        {
            var profile = settings.GetProfile(strongest);
            amplitude = profile.Amplitude;
            frequency = profile.Frequency;
            duration = profile.Duration;
        }
        else
        {
            amplitude = _amplitude;
            frequency = _frequency;
            duration = _shakeDuration;
        }

        _shakeTimer = duration;
        CinemachineBasicMultiChannelPerlin.AmplitudeGain = amplitude;
        CinemachineBasicMultiChannelPerlin.FrequencyGain = frequency;

        _entityManager.DestroyEntity(requestEntities);
        requestEntities.Dispose();
    }
}
