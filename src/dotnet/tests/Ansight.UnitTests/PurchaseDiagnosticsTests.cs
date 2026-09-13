namespace Ansight.UnitTests;

using Ansight.Purchases;
using Ansight.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

public class PurchaseDiagnosticsTests
{
    [Fact]
    public async Task UnknownAndProductionCannotUseAnyEntryPoint()
    {
        var diagnostics = new PurchaseDiagnostics(() => false);
        Assert.False(diagnostics.IsInteropAllowed);
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.Record(new("item", "app", 100, Environment: "sandbox")));
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.RecordProduct(new("item", true, 100)));
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.CreateTransactionReference("token"));
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.ImportStoreKit([], [], diagnostics.CaptureGeneration()));
        Assert.Throws<PurchaseEnvironmentException>(() => PurchaseGooglePlay.Record([], 1, false, diagnostics: diagnostics));
        await Assert.ThrowsAsync<PurchaseEnvironmentException>(() => PurchaseStoreKit.RefreshAsync([], diagnostics));
        foreach (var tool in PurchaseTools.Create(diagnostics)) {
            Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.Execute(tool.Id));
            var availability = await tool.GetAvailabilityAsync(new(null, null));
            var result = await ((IJsonTool)tool).ExecuteAsync(new(new(), new("test", null, null)), CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(PurchaseEnvironmentException.ErrorCode, result.ErrorCode);
            Assert.False(availability.IsAvailable);
            Assert.Equal(PurchaseEnvironmentException.ErrorCode, availability.ReasonCode);
        }
        diagnostics.Clear(); // Local cleanup must remain safe even when unavailable.
        Assert.False(new PurchaseDiagnostics().IsInteropAllowed); // Plain net9.0 cannot establish a store sandbox.
    }

    [Fact]
    public void EnvironmentIsRecheckedAndProductionEvidenceIsRejected()
    {
        var allowed = true;
        var diagnostics = new PurchaseDiagnostics(() => allowed);
        diagnostics.Record(new("item", "store", 100, Environment: "sandbox"));
        allowed = false;
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.Execute("purchases.get_state"));
        allowed = true;
        Assert.Empty(diagnostics.Execute("purchases.get_state")["observations"]!.AsArray());
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.Record(new("item", "store", 100, Environment: "production")));
        Assert.Throws<PurchaseEnvironmentException>(() => diagnostics.ImportStoreKit(
            [new("sandbox", "store", 100, Environment: "sandbox"), new("production", "store", 100, Environment: "production")], [], diagnostics.CaptureGeneration()));
        Assert.Empty(diagnostics.Execute("purchases.get_state")["observations"]!.AsArray());
        Assert.False(new PurchaseDiagnostics(() => throw new Exception()).IsInteropAllowed);
        Assert.True(PurchaseEnvironment.IsAndroidEmulator("ranchu"));
        Assert.True(PurchaseEnvironment.IsAndroidEmulator("goldfish"));
        foreach (var hardware in new string?[] { null, "", "unknown", "test-keys", "generic", "qcom" })
            Assert.False(PurchaseEnvironment.IsAndroidEmulator(hardware));
    }

    [Fact]
    public void ReferencesAreOpaqueAndClearInvalidatesAnInflightImport()
    {
        var diagnostics = new PurchaseDiagnostics(() => true);
        var reference = diagnostics.CreateTransactionReference("raw-purchase-token");
        Assert.Equal(64, reference.Length);
        Assert.Equal(reference, diagnostics.CreateTransactionReference("raw-purchase-token"));
        Assert.NotEqual(reference, new PurchaseDiagnostics(() => true).CreateTransactionReference("raw-purchase-token"));
        var generation = diagnostics.CaptureGeneration();
        diagnostics.Clear();
        Assert.NotEqual(reference, diagnostics.CreateTransactionReference("raw-purchase-token"));
        Assert.Throws<OperationCanceledException>(() => diagnostics.ImportStoreKit([new("item", "store", 100)], [], generation));
        Assert.Empty(diagnostics.Execute("purchases.get_state")["observations"]!.AsArray());
    }

    [Fact]
    public void SharedValidationCases()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "purchase-validation-cases.json")))!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var test in fixture["cases"]!.AsArray()) {
            var diagnostics = new PurchaseDiagnostics(() => true);
            foreach (var row in test!["observations"]!.AsArray()) diagnostics.Record(row!.Deserialize<PurchaseObservation>(options)!);
            var result = diagnostics.Execute("purchases.validate", test["arguments"]!.DeepClone().AsObject(), fixture["now"]!.GetValue<long>());
            Assert.True(ToolSchemaValidator.Validate(PurchaseSchemas.Result, result).IsValid);
            Assert.True(result["status"]!.GetValue<string>() == test["status"]!.GetValue<string>(), test["name"]!.GetValue<string>() + ": " + result);
        }
    }

    [Fact]
    public void RetentionAndCursorGapsAreExplicit()
    {
        var diagnostics = new PurchaseDiagnostics(() => true);
        for (var index = 0; index < 300; index++) diagnostics.Record(new("item", "app", index));
        var result = diagnostics.Execute("purchases.get_events", new() { ["after"] = 0 });
        Assert.Equal(256, result["observations"]!.AsArray().Count);
        Assert.Equal(44, result["droppedEvents"]!.GetValue<long>());
        Assert.True(result["cursorGap"]!.GetValue<bool>());
        Assert.Empty(diagnostics.Execute("purchases.get_events", new() { ["after"] = 300 })["observations"]!.AsArray());
        diagnostics.Clear();
        Assert.Throws<ArgumentException>(() => diagnostics.Execute("purchases.get_events", new() { ["after"] = 300 }));
    }

    [Fact]
    public void AllToolsAreReadOnlyAndReadsDoNotMutateEvidence()
    {
        var diagnostics = new PurchaseDiagnostics(() => true);
        diagnostics.Record(new("item", "app", 100, DeliveryCount: 1));
        var before = diagnostics.Execute("purchases.get_state", now: 100).ToJsonString();
        foreach (var tool in PurchaseTools.Create(diagnostics)) Assert.Equal(ToolPolicy.Read, tool.Policy);
        diagnostics.Execute("purchases.validate", new() { ["productId"] = "item", ["expectedEntitled"] = true }, 100);
        Assert.Equal(before, diagnostics.Execute("purchases.get_state", now: 100).ToJsonString());
    }

    [Fact]
    public void MalformedInputsAndUndeclaredArgumentsAreRejected()
    {
        var diagnostics = new PurchaseDiagnostics(() => true);
        Assert.Throws<ArgumentException>(() => diagnostics.Record(new("", "app", 0)));
        Assert.Throws<ArgumentException>(() => diagnostics.Record(new("item", "arbitrary", 0)));
        Assert.Throws<ArgumentException>(() => diagnostics.Execute("purchases.validate", new() { ["productId"] = "item", ["expectedEntitled"] = true, ["receipt"] = "secret" }));
        Assert.Throws<ArgumentException>(() => diagnostics.Execute("purchases.validate", new() { ["productId"] = "item", ["expectedEntitled"] = true, ["maxAgeMilliseconds"] = 0 }));
    }
}
