using System;

/// <summary>A field that either inherits a shared base value or is explicitly set to its own — including
/// explicitly 0/false/default, which a plain "0 = inherit" sentinel can't distinguish from "not set".
/// Used wherever a per-character (or later, per-variant) SO composes over a shared default.</summary>
[Serializable]
public struct Overridable<T> where T : struct
{
    public bool Override;
    public T Value;

    /// <summary>Resolves to <see cref="Value"/> if overridden, otherwise to <paramref name="baseValue"/>.</summary>
    public T Resolve(T baseValue) => Override ? Value : baseValue;
}
