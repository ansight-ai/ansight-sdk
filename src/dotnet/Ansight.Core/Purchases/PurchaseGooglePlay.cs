namespace Ansight.Purchases;

/// <summary>Binding-neutral adapter for the app's existing Google Play callbacks.</summary>
public static class PurchaseGooglePlay
{
    public static void Record(IEnumerable<string> productIds, int purchaseState, bool acknowledged,
        bool suspended = false, string productType = "unknown", PurchaseDiagnostics? diagnostics = null)
    {
        (diagnostics ?? PurchaseDiagnostics.Shared).EnsureInteropAllowed();
        ArgumentNullException.ThrowIfNull(productIds);
        var state = purchaseState == 1 ? "purchased" : purchaseState == 2 ? "pending" : "unknown";
        foreach (var id in productIds) (diagnostics ?? PurchaseDiagnostics.Shared).Record(new PurchaseObservation(
            id, "store", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), State: state,
            Entitled: state == "unknown" ? null : state == "purchased" && !suspended,
            Acknowledged: acknowledged, ProductType: productType));
    }
}
