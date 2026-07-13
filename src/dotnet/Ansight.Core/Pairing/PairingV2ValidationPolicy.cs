namespace Ansight.Pairing;

/// <summary>
/// Lifetime and clock-skew limits applied to protocol-v2 security material.
/// </summary>
public sealed class PairingV2ValidationPolicy
{
    public static PairingV2ValidationPolicy Default { get; } = new();

    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan MaximumConfigLifetime { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan MaximumGrantLifetime { get; init; } = TimeSpan.FromDays(90);

    public TimeSpan MaximumOfferLifetime { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaximumChallengeLifetime { get; init; } = TimeSpan.FromMinutes(1);
}
