using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class GameOverUIController : MonoBehaviour
{

    [Header("Game Over View")]
    public GameObject GameOverView;
    public TMP_Text GameOverText;
    public float FadeInDuration = 2f;
    public float DisplayDuration = 1f;

    [Header("Summary View")]
    public SummaryView RunSummaryView;


    public void OpenView()
    {
        GameOverView.SetActive(true);
        RunSummaryView.gameObject.SetActive(false);

        SetTextAlpha(0f);

        StartCoroutine(PlaySequenceCoroutine());
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
        RunSummaryView.gameObject.SetActive(true);
        RunSummaryView.RefreshView();
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

        // Set final pour être sûr d'être à la valeur exacte
        color.a = endAlpha;
        GameOverText.color = color;
    }
}
