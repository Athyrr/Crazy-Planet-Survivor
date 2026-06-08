using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

public class HealthHUDWidget : MonoBehaviour
{
    [Header("UI References")]
    public Slider HealthSlider;

    public TMP_Text HealthText;
    public Image HealthImage;

    private Material _healthMaterial;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;

    void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<CoreStats>()
        );

        if (HealthImage != null)
            _healthMaterial = HealthImage.material;
    }

    private void Update()
    {
        if (_playerQuery.IsEmpty)
            return;

        // On s'en fou c'est du feedback
        _playerQuery.CompleteDependency();

        Health playerHealth = _playerQuery.GetSingleton<Health>();
        var playerStats = _playerQuery.GetSingleton<CoreStats>();

        int maxHealth = Mathf.Max(1, Mathf.FloorToInt(playerStats.MaxHealth));
        int current = Mathf.Clamp(playerHealth.Value, 0, maxHealth);
        float ratio = (float)current / maxHealth;

        if (HealthSlider != null)
        {
            HealthSlider.maxValue = maxHealth;
            HealthSlider.value = current;
        }

        if (_healthMaterial != null)
            _healthMaterial.SetFloat("_Value", ratio);

        if (HealthText != null)
            HealthText.text = $"{current} / {maxHealth}";
    }
}
