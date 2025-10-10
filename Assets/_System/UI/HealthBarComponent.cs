using UnityEngine;
using Unity.Entities;
using UnityEngine.UI;
using TMPro;

public class HealthBarComponent : MonoBehaviour
{
    public Slider HealthSlider;
    public TMP_Text HealthText;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;

    void Start()
    {
        if (HealthSlider == null)
            HealthSlider = GetComponent<Slider>();

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<Stats>()
            );
    }

    void Update()
    {
        if (_playerQuery.IsEmpty)
            return;

        Health playerHealth = _playerQuery.GetSingleton<Health>();
        var playerStats = _playerQuery.GetSingleton<Stats>();

        float ratio = Mathf.Clamp01(playerHealth.Value / playerStats.MaxHealth);
        HealthSlider.value = ratio;

        HealthText.text = $"{playerHealth.Value} / {playerStats.MaxHealth}";
    }
}
