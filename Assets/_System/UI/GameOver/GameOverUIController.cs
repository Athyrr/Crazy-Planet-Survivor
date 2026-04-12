using System;
using System.Collections;
using _System.ECS.Authorings.Ressources;
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


    public void OpenView(EEndRunState endState, PlayerRessources ressources)
    {
        gameObject.SetActive(true);
        GameOverView.SetActive(true);
        runSummaryListView.gameObject.SetActive(false);

        StateUpdate(endState, ressources);

        SetTextAlpha(0f);

        StartCoroutine(PlaySequenceCoroutine());
    }

    private void StateUpdate(EEndRunState state, PlayerRessources ressources)
    {
        switch (state)
        {
            case EEndRunState.Success:
                GameOverText.text = "YOUPI";
                GameOverText.color = Color.yellow;
                KeepPersistantRessources(ressources);
                break;

            case EEndRunState.Death:
                GameOverText.text = "YOU DIED (nulos)";
                GameOverText.color = Color.red;
                KeepPersistantRessources(ressources); // todo remove here
                break;

            case EEndRunState.Timeout:
                GameOverText.text = "TIME OUT";
                GameOverText.color = Color.gray;
                KeepPersistantRessources(ressources);
                break;

            default:
                GameOverText.text = "NULOS";
                GameOverText.color = Color.red;
                break;
        }
    }

    private void KeepPersistantRessources(PlayerRessources ressources)
    {
        var saveRessources = SaveManager.GetCurrentSaveAs<Save>().ressources;

        for (int i = 0; i < Enum.GetNames(typeof(ERessourceType)).Length; i++)
            saveRessources.Ressources[i] += ressources.Ressources[i];
        
        SaveManager.ManualSave();
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
