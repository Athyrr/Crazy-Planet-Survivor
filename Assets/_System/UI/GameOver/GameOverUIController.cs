using System.Collections;
using Unity.Entities;
using UnityEngine;
using TMPro;

public class GameOverUIController : UIControllerBase
{
    [Header("Game Over View")]
    public GameObject GameOverView;
    public TMP_Text GameOverText;
    public float FadeInDuration = 2f;
    public float DisplayDuration = 1f;

    [Header("Summary View")]
    public SummaryListView runSummaryListView;

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;
    
    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));
    }

    private void OnEnable()
    {
        //if gameinput is null, create instance
        
    }
    
    public void OpenView(EEndRunState endState, ResourceBufferElement[] resources)
    {
        gameObject.SetActive(true);
        GameOverView.SetActive(true);
        runSummaryListView.gameObject.SetActive(false);

        StateUpdate(endState, resources);

        SetTextAlpha(0f);

        StartCoroutine(PlaySequenceCoroutine());
    }

    private void StateUpdate(EEndRunState state, ResourceBufferElement[] resources)
    {
        switch (state)
        {
            case EEndRunState.Success:
                GameOverText.text = "YOUPI";
                GameOverText.color = Color.yellow;
                KeepPersistantRessources(resources);
                break;

            case EEndRunState.Death:
                GameOverText.text = "YOU DIED (nulos)";
                GameOverText.color = Color.red;
                KeepPersistantRessources(resources); // todo remove here
                break;

            case EEndRunState.Timeout:
                GameOverText.text = "TIME OUT";
                GameOverText.color = Color.gray;
                KeepPersistantRessources(resources);
                break;

            default:
                GameOverText.text = "NULOS";
                GameOverText.color = Color.red;
                break;
        }
    }

    private void KeepPersistantRessources(ResourceBufferElement[] runResources)
    {
        if (_gameStateQuery.IsEmpty)
        {
            Debug.LogWarning("GameState entity not found, cannot persist resources.");
            return;
        }

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        var metaResources = _entityManager.GetBuffer<ResourceBufferElement>(gameStateEntity);

        foreach (var res in runResources)
        {
            if (res.Value > 0)
                metaResources.AddOrDeduct(res.Type, res.Value);
        }

        metaResources.Save();
    }

    private void SetTextAlpha(float alpha)
    {
        Color color = GameOverText.color;
        color.a = alpha;
        GameOverText.color = color;
    }

    private IEnumerator PlaySequenceCoroutine()
    {

        yield return StartCoroutine(FadeTextAlpha(0f, 1f, FadeInDuration));

        yield return new WaitForSeconds(DisplayDuration);

        GameOverView.SetActive(false);
        runSummaryListView.gameObject.SetActive(true);
        runSummaryListView.RefreshView();
    }

    private IEnumerator FadeTextAlpha(float startAlpha, float endAlpha, float duration)
    {
        float elapsedTime = 0f;
        Color color = GameOverText.color;

        // Set initial
        color.a = startAlpha;
        GameOverText.color = color;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float alpha = Mathf.Lerp(startAlpha, endAlpha, elapsedTime / duration);

            color.a = alpha;
            GameOverText.color = color;

            yield return null;
        }

        color.a = endAlpha;
        GameOverText.color = color;
    }
}
