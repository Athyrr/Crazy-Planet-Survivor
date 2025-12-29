using Unity.Cinemachine;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class ShakeFeedbackComponent : MonoBehaviour
{
    public CinemachineBasicMultiChannelPerlin CinemachineBasicMultiChannelPerlin;

    private EntityManager _entityManager;
    private EntityQuery _shakeRequestQuery;

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
        Entity shakeRequestEntity = requestEntities[0];
        //var shakeRequest = _entityManager.GetComponentData<ShakeFeedbackRequest>(shakeRequestEntity);

        _shakeTimer = _shakeDuration;
        CinemachineBasicMultiChannelPerlin.AmplitudeGain =  _amplitude;
        CinemachineBasicMultiChannelPerlin.FrequencyGain = _frequency;

        _entityManager.DestroyEntity(requestEntities);
        requestEntities.Dispose();
    }
}
