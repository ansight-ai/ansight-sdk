namespace Ansight.Purchases;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>StoreKit 2 observation through Ansight's Swift core and slim Apple binding.</summary>
public static class PurchaseStoreKit
{
    public static async Task RefreshAsync(IEnumerable<string> productIds, PurchaseDiagnostics? diagnostics = null, CancellationToken cancellationToken = default)
    {
        (diagnostics ?? PurchaseDiagnostics.Shared).EnsureInteropAllowed();
        ArgumentNullException.ThrowIfNull(productIds);
        var ids = productIds.ToArray();
        if (ids.Length is < 1 or > 100 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256))
            throw new ArgumentException("Supply 1–100 product IDs.", nameof(productIds));
        cancellationToken.ThrowIfCancellationRequested();
#if IOS || MACCATALYST
        diagnostics ??= PurchaseDiagnostics.Shared;
        var generation = diagnostics.CaptureGeneration();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Native.Apple.ANSDotNetRuntime.PurchaseCommand(JsonSerializer.Serialize(new { action = "refreshStoreKit", productIds = ids }), json => completion.TrySetResult(json));
        var response = JsonNode.Parse(await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))!.AsObject();
        if (response["error"]?.GetValue<string>() == PurchaseEnvironmentException.ErrorCode) throw new PurchaseEnvironmentException();
        if (response.ContainsKey("error")) throw new InvalidOperationException("StoreKit observation failed.");
        var rows = response["observations"]!.Deserialize<PurchaseObservation[]>(options)!;
        var latest = rows.Reverse().Where(o => o.Source == "store" && ids.Contains(o.ProductId))
            .GroupBy(o => o.ProductId).Select(g => g.OrderByDescending(o => o.ObservedAt).First());
        var metadata = response["products"]!.Deserialize<PurchaseProduct[]>(options)!.Where(p => ids.Contains(p.ProductId));
        diagnostics.ImportStoreKit(latest, metadata, generation);
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("StoreKit requires an Apple target. Use the base purchase reporting APIs with your platform's billing service.");
#endif
    }
}
