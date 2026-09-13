namespace Ansight.Purchases;

/// <summary>A sanitized observation. Store, app, and backend evidence remain separate.</summary>
public sealed record PurchaseObservation(
    string ProductId,
    string Source,
    long ObservedAt,
    string State = "unknown",
    string Verification = "unknown",
    bool? Entitled = null,
    int? DeliveryCount = null,
    bool? Finished = null,
    bool? Acknowledged = null,
    bool? Consumed = null,
    long? ExpiresAt = null,
    string Environment = "unknown",
    string ProductType = "unknown",
    string? TransactionRef = null,
    long? GracePeriodExpiresAt = null);
