using UnityEngine;

/// <summary>
/// Direction this navigation edge produced (None = no new edge this event).
/// </summary>
public enum NavDirection { None, Up, Down, Left, Right }

/// <summary>
/// Turns a noisy analog 2D navigation value (PassThrough stick/d-pad/keyboard) into a single discrete
/// step per push. A push must cross <see cref="Enter"/> to latch a direction; the stick must fall back
/// below <see cref="Exit"/> (hysteresis) before another step can fire, so analog jitter or the ramp of
/// a stick can't register several steps from one flick. Flicking straight to a new direction also steps
/// once. <see cref="Evaluate"/> returns the direction to act on, or None when there's no new edge.
/// </summary>
public struct NavRepeatFilter
{
    private const float Enter = 0.5f;
    private const float Exit = 0.35f;

    private NavDirection _current;

    public NavDirection Evaluate(Vector2 v)
    {
        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);
        float threshold = _current != NavDirection.None ? Exit : Enter;

        NavDirection dir;
        if (Mathf.Max(ax, ay) < threshold)
            dir = NavDirection.None;
        else if (ay >= ax)
            dir = v.y > 0f ? NavDirection.Up : NavDirection.Down;
        else
            dir = v.x > 0f ? NavDirection.Right : NavDirection.Left;

        if (dir == _current)
            return NavDirection.None; // held in the same zone (or still neutral) -> no new step

        _current = dir;
        return dir; // a new edge; callers ignore None (released back to neutral)
    }

    public void Reset() => _current = NavDirection.None;
}
