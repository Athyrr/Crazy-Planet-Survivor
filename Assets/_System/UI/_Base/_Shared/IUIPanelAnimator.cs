using System;
using PrimeTween;
using UnityEngine;

/// <summary>
/// A UI panel entrance/exit animator. <see cref="Show"/> brings the panel on-screen, <see cref="Hide"/>
/// takes it off-screen and invokes the optional callback when the animation completes. Implemented by
/// <see cref="UISlidePanel"/> (anchored-position slide) and <see cref="UIFadePanel"/> (CanvasGroup fade).
/// </summary>
public interface IUIPanelAnimator
{
    Tween Show();
    Tween Hide(Action onComplete = null);
}

/// <summary>
/// Drives a set of <see cref="IUIPanelAnimator"/> components (assigned as a MonoBehaviour[] in the
/// inspector) as one group — e.g. a shop whose list / detail / title slide in from different edges
/// while the background fades in. Each element must implement <see cref="IUIPanelAnimator"/>.
/// </summary>
public static class UIPanelGroup
{
    public static void Show(MonoBehaviour[] animators)
    {
        if (animators == null)
            return;

        foreach (var mb in animators)
            if (mb is IUIPanelAnimator a)
                a.Show();
    }

    /// <summary>
    /// Hides every animator and invokes <paramref name="onAllComplete"/> once the last one finishes.
    /// Invokes immediately when there is nothing to animate (so callers still deactivate cleanly).
    /// </summary>
    public static void Hide(MonoBehaviour[] animators, Action onAllComplete)
    {
        int remaining = 0;
        if (animators != null)
            foreach (var mb in animators)
                if (mb is IUIPanelAnimator)
                    remaining++;

        if (remaining == 0)
        {
            onAllComplete?.Invoke();
            return;
        }

        foreach (var mb in animators)
        {
            if (mb is IUIPanelAnimator a)
            {
                a.Hide(() =>
                {
                    remaining--;
                    if (remaining == 0)
                        onAllComplete?.Invoke();
                });
            }
        }
    }
}
