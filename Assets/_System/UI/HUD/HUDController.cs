using UnityEngine;

public class HUDController : UIControllerBase
{
    // Bumped on every Show/Hide. A deferred hide-completion checks it, so a slide-out that was interrupted
    // by a Show no longer switches the HUD back off after the fact.
    private int _generation;

    public void ShowHUD()
    {
        _generation++;

        if (gameObject.activeSelf)
        {
            // Already visible (interrupting an in-progress hide): OnEnable won't refire, so re-play the
            // slide-in directly, otherwise the panels would finish their slide-out and vanish.
            foreach (var animator in GetComponentsInChildren<IUIPanelAnimator>())
                animator.Show();
        }
        else
        {
            gameObject.SetActive(true); // OnEnable -> panels slide in via their Play On Enable flag
        }
    }

    // The out-animation has to be driven explicitly: an object can't tween while it is being deactivated.
    // Animate every child panel out, then deactivate the HUD once the last one finishes — but only if no
    // Show/Hide happened in the meantime (otherwise a resume that re-showed the HUD would be undone).
    // onComplete fires after the slide-out finishes (used to sequence the next panel in).
    public void HideHUD(System.Action onComplete = null)
    {
        int generation = ++_generation;
        var animators = System.Array.ConvertAll(
            GetComponentsInChildren<IUIPanelAnimator>(), a => (MonoBehaviour)a);
        UIPanelGroup.Hide(animators, () =>
        {
            if (generation == _generation)
                gameObject.SetActive(false);
            onComplete?.Invoke();
        });
    }
}
