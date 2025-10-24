using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

public class ExpBarComponent : MonoBehaviour
{
    public Slider ExpSlider;
    public TMP_Text ExpText;
    public TMP_Text LevelText;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;

    void Start()
    {
        if (ExpSlider == null)
            ExpSlider = GetComponent<Slider>();

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerExperience>());
    }

    void Update()
    {
        if (_playerQuery.IsEmpty)
            return;

        PlayerExperience playerExperience = _playerQuery.GetSingleton<PlayerExperience>();

        float ratio = Mathf.Clamp01(playerExperience.Experience / playerExperience.NextLevelExperienceRequired);
        ExpSlider.value = ratio;
        LevelText.text = $"{playerExperience.Level}";
        ;
        ExpText.text = $"{playerExperience.Experience} / {playerExperience.NextLevelExperienceRequired}";
    }
}
