namespace Ansight.Purchases;

public sealed record PurchaseProduct(string ProductId, bool Available, long ObservedAt,
    string? DisplayPrice = null, string? Price = null, string? CurrencyCode = null, string ProductType = "unknown");
