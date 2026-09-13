namespace Ansight.Purchases;

using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

/// <summary>Bounded passive purchase evidence and validation. Never grants or finishes purchases.</summary>
public sealed class PurchaseDiagnostics
{
    private readonly Func<bool> environmentAllowed;
    public PurchaseDiagnostics() : this(PurchaseEnvironment.IsAllowed) { }
    internal PurchaseDiagnostics(Func<bool> environmentAllowed) { this.environmentAllowed = environmentAllowed; }
    public bool IsInteropAllowed { get { try { return environmentAllowed(); } catch { return false; } } }
    public void EnsureInteropAllowed()
    {
        if (IsInteropAllowed) return;
        Clear();
        throw new PurchaseEnvironmentException();
    }

    public static PurchaseDiagnostics Shared { get; } = new();
    private readonly object gate = new();
    private string referenceSalt = Guid.NewGuid().ToString();
    private readonly List<PurchaseObservation> observations = new();
    private long dropped;
    private long generation;
    private readonly List<PurchaseProduct> products = new();
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    internal static readonly string[] toolIds = ["purchases.query_products", "purchases.get_state", "purchases.query_transactions", "purchases.get_entitlements", "purchases.get_events", "purchases.validate"];

    public string CreateTransactionReference(string identifier)
    {
        EnsureInteropAllowed();
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (identifier.Length > 8192) throw new ArgumentException("Identifier too large.");
        lock (gate) return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(referenceSalt + identifier)));
    }

    public void Record(PurchaseObservation observation)
    {
        EnsureInteropAllowed();
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Environment == "production") throw new PurchaseEnvironmentException();
        if (string.IsNullOrWhiteSpace(observation.ProductId) || observation.ProductId.Length > 256
            || !new[] { "store", "app", "backend" }.Contains(observation.Source)
            || !new[] { "unknown", "pending", "purchased", "cancelled", "expired", "revoked", "failed", "gracePeriod", "billingRetry" }.Contains(observation.State)
            || !new[] { "unknown", "verified", "unverified" }.Contains(observation.Verification)
            || !new[] { "unknown", "xcode", "sandbox", "production" }.Contains(observation.Environment)
            || !new[] { "unknown", "consumable", "nonConsumable", "subscription", "nonRenewingSubscription" }.Contains(observation.ProductType)
            || (observation.TransactionRef != null && (observation.TransactionRef.Length != 64 || observation.TransactionRef.Any(c => !"0123456789abcdef".Contains(c))))
            || observation.ObservedAt < 0 || observation.ExpiresAt < 0 || observation.GracePeriodExpiresAt < 0 || observation.DeliveryCount < 0)
            throw new ArgumentException("Invalid purchase observation.", nameof(observation));
        lock (gate)
        {
            if (observations.Count == 256) { observations.RemoveAt(0); dropped++; }
            observations.Add(observation);
        }
    }

    /// <summary>Clear evidence on account switch; this never resets the store or app entitlement.</summary>
    internal long CaptureGeneration() { lock (gate) return generation; }

    internal void ImportStoreKit(IEnumerable<PurchaseObservation> rows, IEnumerable<PurchaseProduct> metadata, long expectedGeneration)
    {
        EnsureInteropAllowed();
        var batch = rows.ToArray();
        if (batch.Any(row => row.Environment == "production")) throw new PurchaseEnvironmentException();
        lock (gate) {
            if (generation != expectedGeneration) throw new OperationCanceledException("Purchase diagnostics were cleared during refresh.");
            foreach (var row in batch) Record(row);
            foreach (var product in metadata) RecordProduct(product);
        }
    }

    public void RecordProduct(PurchaseProduct value)
    {
        EnsureInteropAllowed();
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.ProductId) || value.ProductId.Length > 256 || value.ObservedAt < 0
            || !new[] { "unknown", "consumable", "nonConsumable", "subscription", "nonRenewingSubscription" }.Contains(value.ProductType)
            || new[] { value.DisplayPrice, value.Price, value.CurrencyCode }.Any(s => s?.Length > 256))
            throw new ArgumentException("Invalid purchase product.");
        lock (gate) {
            products.RemoveAll(p => p.ProductId == value.ProductId);
            if (products.Count == 256) products.RemoveAt(0);
            products.Add(value);
        }
    }

    public void Clear()
    {
        lock (gate) { observations.Clear(); products.Clear(); dropped = 0; generation++; referenceSalt = Guid.NewGuid().ToString(); }
    }

    private static long Integer(JsonNode? node, long fallback)
    {
        if (node is null) return fallback;
        if (node is JsonValue value) {
            if (value.TryGetValue<long>(out var number)) return number;
            if (value.TryGetValue<int>(out var small)) return small;
        }
        throw new ArgumentException("Expected an integer.");
    }

    public JsonObject Execute(string toolId, JsonObject? arguments = null, long? now = null)
    {
        EnsureInteropAllowed();
        arguments ??= new();
        if (!toolIds.Contains(toolId)) throw new ArgumentException("Unknown purchase tool.");
        var allowed = toolId == "purchases.validate"
            ? new[] { "productId", "transactionRef", "expectedEntitled", "requireVerified", "maxAgeMilliseconds", "requireBackendVerification", "expectedDeliveryCount" }
            : toolId == "purchases.get_events" ? new[] { "after" } : new[] { "productId", "transactionRef" };
        if (arguments.Any(p => !allowed.Contains(p.Key))) throw new ArgumentException("Unexpected purchase argument.");
        var product = arguments["productId"]?.GetValue<string>();
        if (product != null && (string.IsNullOrWhiteSpace(product) || product.Length > 256)) throw new ArgumentException("Invalid productId.");
        var reference = arguments["transactionRef"]?.GetValue<string>();
        if (reference != null && (reference.Length != 64 || reference.Any(c => !"0123456789abcdef".Contains(c)))) throw new ArgumentException("Invalid transactionRef.");
        var time = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (gate)
        {
            var rows = observations.Where(o => (product == null || o.ProductId == product) && (reference == null || o.TransactionRef == reference)).ToArray();
            var result = new JsonObject {
                ["schema"] = "ansight.purchases.v1", ["capturedAt"] = time,
                ["droppedEvents"] = dropped, ["coverage"] = "observedOnly",
                ["observations"] = JsonSerializer.SerializeToNode(rows, jsonOptions),
                ["products"] = JsonSerializer.SerializeToNode(products.Where(p => product == null || p.ProductId == product), jsonOptions)
            };
            if (toolId == "purchases.get_events")
            {
                var after = Integer(arguments["after"], 0);
                if (after < 0 || after > dropped + observations.Count) throw new ArgumentException("Invalid event cursor.");
                result["observations"] = JsonSerializer.SerializeToNode(observations.Skip((int)Math.Max(0, after - dropped)), jsonOptions);
                result["nextCursor"] = dropped + observations.Count;
                result["cursorGap"] = after < dropped;
            }
            if (toolId == "purchases.validate")
            {
                if (product == null || arguments["expectedEntitled"] is null) throw new ArgumentException("productId and expectedEntitled are required.");
                var expected = arguments["expectedEntitled"]!.GetValue<bool>();
                var verify = arguments["requireVerified"]?.GetValue<bool>() ?? true;
                var maxAge = Integer(arguments["maxAgeMilliseconds"], 60_000);
                if (maxAge < 1 || maxAge > 86_400_000) throw new ArgumentException("Invalid freshness bound.");
                var backend = arguments["requireBackendVerification"]?.GetValue<bool>() ?? false;
                var delivery = arguments["expectedDeliveryCount"]?.GetValue<int>();
                if (delivery < 0) throw new ArgumentException("Invalid delivery count.");
                var checks = new JsonArray();
                foreach (var source in backend ? new[] { "store", "app", "backend" } : new[] { "store", "app" })
                {
                    var observation = rows.Reverse().Where(o => o.Source == source).OrderByDescending(o => o.ObservedAt).FirstOrDefault();
                    var status = "inconclusive";
                    var reason = "missing_evidence";
                    if (observation != null)
                    {
                        var age = time - observation.ObservedAt;
                        if (age < 0 || age > maxAge) reason = "stale_evidence";
                        else if (observation.Entitled is null && !(source == "store" && observation.ProductType == "consumable")) reason = "unknown_entitlement";
                        else {
                            var active = observation.Entitled ?? (observation.State == "purchased");
                            var effectiveExpiry = observation.State == "gracePeriod" ? observation.GracePeriodExpiresAt : observation.ExpiresAt;
                            if (source == "store" && ((effectiveExpiry is long expiry && expiry <= time) || observation.State is "revoked" or "expired" or "pending" or "cancelled" or "failed" or "billingRetry")) active = false;
                            status = active == expected ? "pass" : "fail";
                            reason = active == expected ? "entitlement_matches" : "entitlement_mismatch";
                            if (source == "store" && observation.State == "gracePeriod" && effectiveExpiry == null && status == "pass" && expected) { status = "inconclusive"; reason = "missing_grace_period_expiry"; }
                            if (expected && source == "store" && observation.State is not ("purchased" or "gracePeriod")) { status = "fail"; reason = "store_not_purchased"; }
                            if (status != "fail" && ((expected && source == "store" && verify && !backend) || (source == "backend" && backend)) && observation.Verification != "verified") {
                                status = observation.Verification == "unverified" ? "fail" : "inconclusive"; reason = "verification_" + observation.Verification;
                            }
                            if (source == "app" && delivery != null && status != "fail") {
                                status = observation.DeliveryCount == null ? "inconclusive" : observation.DeliveryCount == delivery ? status : "fail";
                                if (observation.DeliveryCount != delivery) reason = observation.DeliveryCount == null ? "missing_delivery_count" : "delivery_count_mismatch";
                            }
                        }
                    }
                    if (observation != null && time >= observation.ObservedAt && time - observation.ObservedAt <= maxAge
                        && observation.Verification == "unverified"
                        && ((expected && source == "store" && verify && !backend) || (source == "backend" && backend))) {
                        status = "fail"; reason = "verification_unverified";
                    }
                    checks.Add(new JsonObject { ["source"] = source, ["status"] = status, ["reason"] = reason });
                }
                var statuses = checks.Select(c => c!["status"]!.GetValue<string>()).ToArray();
                result["status"] = statuses.Contains("fail") ? "fail" : statuses.Contains("inconclusive") ? "inconclusive" : "pass";
                result["checks"] = checks;
            }
            return result;
        }
    }
}
