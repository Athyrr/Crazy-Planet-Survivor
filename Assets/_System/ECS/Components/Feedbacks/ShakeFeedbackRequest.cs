using Unity.Entities;

public struct ShakeFeedbackRequest : IComponentData
{
    /// <summary>
    /// Damage-source category that determines the shake profile (amplitude/frequency/duration)
    /// resolved from the CameraShakeSettings SO. <see cref="EDamageShakeSource.None"/> uses the
    /// SO Default profile (e.g. the player-death shake).
    /// </summary>
    public EDamageShakeSource Source;
}
