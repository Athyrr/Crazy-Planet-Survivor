using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

public class UI_PlayerHealthBarComponent : MonoBehaviour
{
    public TMP_Text HealthText;
    public Image HealthImage;

    private Material _healthMaterial;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;


    void Start()
    {
        if (HealthImage == null)
            Debug.LogError("Image not found", this);

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<Stats>()
            );

        _healthMaterial = HealthImage.material;
        if (_healthMaterial == null)
            Debug.LogError("Image material not found", this);

    }

    void Update()
    {
        if (_playerQuery.IsEmpty)
            return;

        Health playerHealth = _playerQuery.GetSingleton<Health>();
        var playerStats = _playerQuery.GetSingleton<Stats>();

        float ratio = Mathf.Clamp01(playerHealth.Value / playerStats.MaxHealth);

        _healthMaterial.SetFloat("_Value", ratio);

        HealthText.text = $"{playerHealth.Value} / {playerStats.MaxHealth}";
        //HealthText.text = $"{playerHealth.Value}";
    }
}
