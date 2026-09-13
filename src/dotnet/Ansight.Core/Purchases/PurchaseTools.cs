namespace Ansight.Purchases;

using Ansight.Tools;

public static class PurchaseTools
{
    public static Options.OptionsBuilder WithPurchaseTools(this Options.OptionsBuilder builder, PurchaseDiagnostics? diagnostics = null)
        => builder.AddTools(Create(diagnostics));

    public static ITool[] Create(PurchaseDiagnostics? diagnostics = null)
        => PurchaseDiagnostics.toolIds.Select(id => (ITool)new PurchaseTool(id, diagnostics ?? PurchaseDiagnostics.Shared)).ToArray();
}

internal sealed class PurchaseTool(string id, PurchaseDiagnostics diagnostics) : IJsonTool
{
    public string Id => id;
    public string Name => id;
    public string Category => "purchases";
    public ToolPolicy Policy => ToolPolicy.Read;
    public string Description => "Inspect bounded purchase evidence reported by the store and application. Missing evidence is inconclusive. Does not grant, acknowledge, consume, or finish purchases.";
    public string Keywords => "purchase storekit billing entitlement validation";
    public ToolSchema ArgumentsSchema => Id == "purchases.validate"
        ? ToolSchema.Object(properties: new Dictionary<string, ToolSchema> {
            ["transactionRef"] = ToolSchema.String(), ["productId"] = ToolSchema.String(), ["expectedEntitled"] = ToolSchema.Boolean(),
            ["requireVerified"] = ToolSchema.Boolean(), ["requireBackendVerification"] = ToolSchema.Boolean(), ["expectedDeliveryCount"] = ToolSchema.Integer(), ["maxAgeMilliseconds"] = ToolSchema.Integer()
        }, required: ["productId", "expectedEntitled"])
        : Id == "purchases.get_events"
        ? ToolSchema.Object(properties: new Dictionary<string, ToolSchema> { ["after"] = ToolSchema.Integer() })
        : ToolSchema.Object(properties: new Dictionary<string, ToolSchema> { ["transactionRef"] = ToolSchema.String(), ["productId"] = ToolSchema.String() });
    public ToolSchema ResultSchema => PurchaseSchemas.Result;

    public ValueTask<ToolAvailability> GetAvailabilityAsync(ToolAvailabilityContext context)
        => ValueTask.FromResult(diagnostics.IsInteropAllowed ? ToolAvailability.Available
            : ToolAvailability.Unavailable(PurchaseEnvironmentException.ErrorCode,
                "Purchase interop is restricted to simulators and emulators.", requiredState: "simulator"));

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return Task.FromResult(ToolResult.Success(diagnostics.Execute(Id, invocation.Arguments))); }
        catch (PurchaseEnvironmentException error) {
            return Task.FromResult(ToolResult.Failure(error.Message, errorCode: PurchaseEnvironmentException.ErrorCode));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) {
            return Task.FromResult(ToolResult.Failure("Invalid purchase arguments.", errorCode: "purchases_invalid_arguments"));
        }
    }
}
