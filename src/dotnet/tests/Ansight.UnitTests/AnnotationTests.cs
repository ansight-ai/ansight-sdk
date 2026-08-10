using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Annotations;
using Ansight.Artifacts;
using Ansight.Maui;
using Ansight.OfflineCapture;
using Ansight.Tools;
using Ansight.Tools.VisualTree;

namespace Ansight.UnitTests;

public sealed class AnnotationTests
{
    [Fact]
    public void AnnotationEditorModel_CreatesMovesResizesDeletesAndUndoesFreeDraw()
    {
        var editor = new AnnotationEditorModel
        {
            DrawingTool = AnnotationDrawingTool.FreeDraw
        };
        editor.PointerDown(new AnnotationPoint(0.1, 0.2), 0.02);
        editor.PointerMoved(new AnnotationPoint(0.2, 0.25));
        editor.PointerUp(new AnnotationPoint(0.3, 0.4));

        var freeDraw = Assert.Single(editor.Shapes);
        Assert.Equal(AnnotationShapeKind.FreeDraw, freeDraw.Kind);
        Assert.Equal(3, freeDraw.Points.Count);
        Assert.True(editor.CanUndo);
        editor.SetSelectedText("The footer overlaps this control.");

        editor.DrawingTool = AnnotationDrawingTool.Select;
        editor.PointerDown(new AnnotationPoint(0.2, 0.25), 0.03);
        editor.PointerUp(new AnnotationPoint(0.3, 0.35));
        freeDraw = Assert.Single(editor.Shapes);
        Assert.Equal(0.2, freeDraw.X, precision: 6);
        Assert.Equal(0.3, freeDraw.Y, precision: 6);
        Assert.Equal("The footer overlaps this control.", freeDraw.Text);

        editor.PointerDown(new AnnotationPoint(freeDraw.X + freeDraw.Width, freeDraw.Y + freeDraw.Height), 0.03);
        editor.PointerUp(new AnnotationPoint(0.7, 0.8));
        freeDraw = Assert.Single(editor.Shapes);
        Assert.Equal(0.5, freeDraw.Width, precision: 6);
        Assert.Equal(0.5, freeDraw.Height, precision: 6);

        editor.DeleteSelected();
        Assert.Empty(editor.Shapes);
        editor.Undo();
        Assert.Single(editor.Shapes);
        editor.Undo();
        Assert.Equal(0.2, Assert.Single(editor.Shapes).Width, precision: 6);
    }

    [Fact]
    public async Task AnnotationBundle_SerializesFreeDrawPath()
    {
        using var tempDirectory = new TemporaryAnnotationDirectory();
        var runtime = new RuntimeImpl(Options.CreateBuilder().Build());
        var service = new AnnotationService(
            runtime,
            new AnnotationOptionsBuilder()
                .WithoutScreenshot()
                .WithoutVisualTrees()
                .WithOutboxDirectory(tempDirectory.Path)
                .Build());

        var result = await service.CaptureAsync(new AnnotationCaptureRequest
        {
            Shapes =
            [
                new AnnotationShape(
                [
                    new AnnotationPoint(0.1, 0.2),
                    new AnnotationPoint(0.2, 0.3),
                    new AnnotationPoint(0.4, 0.35)
                ])
                {
                    Text = "The save icon is clipped."
                }
            ]
        }, CancellationToken.None);

        using var archive = ZipFile.OpenRead(result.OutboxPath!);
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
        using var manifest = await ReadJsonAsync(manifestEntry);
        var shape = Assert.Single(manifest.RootElement.GetProperty("shapes").EnumerateArray());
        Assert.Equal("freeDraw", shape.GetProperty("kind").GetString());
        Assert.Equal(3, shape.GetProperty("points").GetArrayLength());
        Assert.Equal("The save icon is clipped.", shape.GetProperty("text").GetString());
    }

    [Fact]
    public async Task PresentAsync_CapturesEvidenceBeforeEditorAndRetainsRequestTimestamp()
    {
        using var tempDirectory = new TemporaryAnnotationDirectory();
        var requestedAtUtc = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        var visualTreeCapturedAtUtc = requestedAtUtc.AddMilliseconds(250);
        var timeProvider = new MutableTimeProvider(requestedAtUtc);
        var provider = new OrderingVisualTreeProvider(visualTreeCapturedAtUtc);
        var hook = new CaptureTimestampHook();
        using var providerRegistration = VisualTreeProviderRegistry.Register(provider);
        var service = new AnnotationService(
            new RuntimeImpl(Options.CreateBuilder().Build()),
            new AnnotationOptionsBuilder()
                .WithoutScreenshot()
                .WithOutboxDirectory(tempDirectory.Path)
                .AddHook(hook)
                .Build(),
            timeProvider,
            (screenshot, overlayHost, cancellationToken) =>
            {
                Assert.Null(screenshot);
                Assert.True(provider.WasCaptured);
                timeProvider.UtcNow = requestedAtUtc.AddMinutes(10);
                return Task.FromResult(AnnotationOverlayResult.Submitted(new AnnotationCaptureRequest
                {
                    Feedback = "Captured before this editor opened."
                }));
            });

        var result = await service.PresentAsync(overlayHost: null, CancellationToken.None);

        Assert.Equal(AnnotationCaptureStatus.Queued, result.Status);
        Assert.Equal(requestedAtUtc, hook.CapturedAtUtc);
        var pending = await new AnnotationOutbox(tempDirectory.Path).LoadPendingAsync(CancellationToken.None);
        var bundle = Assert.Single(pending).Bundle;
        Assert.Equal(requestedAtUtc, bundle.CapturedAtUtc);

        using var archive = ZipFile.OpenRead(result.OutboxPath!);
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
        using var manifest = await ReadJsonAsync(manifestEntry);
        Assert.Equal(requestedAtUtc, manifest.RootElement.GetProperty("capturedAtUtc").GetDateTimeOffset());
        var orderingTree = Assert.Single(
            manifest.RootElement.GetProperty("visualTrees").EnumerateArray(),
            tree => tree.GetProperty("source").GetString() == OrderingVisualTreeProvider.SourceName);
        Assert.Equal(
            visualTreeCapturedAtUtc,
            orderingTree.GetProperty("capturedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task CaptureAsync_CapturesRegisteredTreesHooksAndArtifacts_WhenOtherEvidenceIsUnavailable()
    {
        using var tempDirectory = new TemporaryAnnotationDirectory();
        using var providerRegistration = VisualTreeProviderRegistry.Register(new StubVisualTreeProvider("test"));
        var annotationOptions = new AnnotationOptionsBuilder()
            .WithoutScreenshot()
            .WithOutboxDirectory(tempDirectory.Path)
            .AddHook(new StubAnnotationHook())
            .Build();
        var runtime = new RuntimeImpl(Options.CreateBuilder().Build());
        var service = new AnnotationService(runtime, annotationOptions);

        var result = await service.CaptureAsync(new AnnotationCaptureRequest
        {
            Feedback = "The checkout button is clipped.",
            Shapes = [new AnnotationShape(AnnotationShapeKind.Rectangle, 0.1, 0.2, 0.3, 0.4)]
        }, CancellationToken.None);

        Assert.Equal(AnnotationCaptureStatus.Queued, result.Status);
        Assert.NotNull(result.OutboxPath);
        Assert.True(File.Exists(result.OutboxPath));
        var pending = await new AnnotationOutbox(tempDirectory.Path).LoadPendingAsync(CancellationToken.None);
        Assert.Equal(result.AnnotationId, Assert.Single(pending).Bundle.AnnotationId);
        Assert.Contains(result.Evidence, evidence =>
            evidence.Id == "visual-tree:test" && evidence.Status == AnnotationEvidenceStatus.Captured);

        using var archive = ZipFile.OpenRead(result.OutboxPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "evidence/visual-trees/test.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "artifacts/000-diagnostics.txt");
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
        using var manifest = await ReadJsonAsync(manifestEntry);
        Assert.Equal("ansight.annotation.bundle.v1", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal("The checkout button is clipped.", manifest.RootElement.GetProperty("feedback").GetString());
        Assert.Equal("checkout", manifest.RootElement.GetProperty("customData").GetProperty("flow").GetString());
    }

    [Fact]
    public async Task OfflineCaptureSink_WritesBundleAndIndexOutsideBoundedTelemetryQueue()
    {
        using var tempDirectory = new TemporaryAnnotationDirectory();
        var runtime = new RuntimeImpl(Options.CreateBuilder().Build());
        await using var controller = new OfflineCaptureController(runtime, new OfflineCaptureOptions
        {
            RootDirectory = Path.Combine(tempDirectory.Path, ".ansight"),
            SessionJpegCaptureEnabledOverride = false
        });
        var session = await controller.StartAsync();
        var bundle = new AnnotationBundle(Guid.CreateVersion7(), DateTimeOffset.UtcNow, [1, 2, 3, 4]);

        var sinkResult = await controller.SubmitAsync(bundle, CancellationToken.None);
        await controller.StopAsync();

        Assert.True(sinkResult.IsSuccess);
        var bundlePath = Assert.Single(Directory.GetFiles(
            Path.Combine(session.DirectoryPath, "annotations", "bundles"),
            "*.ansightannotation"));
        Assert.Equal(bundle.Bytes.ToArray(), await File.ReadAllBytesAsync(bundlePath));
        var indexLine = Assert.Single(await File.ReadAllLinesAsync(
            Path.Combine(session.DirectoryPath, "annotations", "index.jsonl")));
        Assert.Contains(bundle.AnnotationId.ToString("N"), indexLine);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(session.DirectoryPath, "manifest.json")));
        Assert.Equal(1, manifest.RootElement.GetProperty("AnnotationCount").GetInt64());
    }

    [Fact]
    public async Task CaptureAsync_ContinuesWhenEvidenceIsDeniedAndAHookFails()
    {
        using var tempDirectory = new TemporaryAnnotationDirectory();
        var annotationOptions = new AnnotationOptionsBuilder()
            .WithoutScreenshot()
            .WithOutboxDirectory(tempDirectory.Path)
            .WithEvidencePolicy(new DenyVisualTreePolicy())
            .AddHook(new ThrowingAnnotationHook())
            .Build();
        var service = new AnnotationService(
            new RuntimeImpl(Options.CreateBuilder().Build()),
            annotationOptions);

        var result = await service.CaptureAsync(
            new AnnotationCaptureRequest { Feedback = "Still submit this." },
            CancellationToken.None);

        Assert.Equal(AnnotationCaptureStatus.Queued, result.Status);
        Assert.NotEmpty(result.Evidence);
        Assert.All(
            result.Evidence.Where(item => item.Kind == AnnotationEvidenceKind.VisualTree),
            item => Assert.Equal(AnnotationEvidenceStatus.NotPermitted, item.Status));
        using var archive = ZipFile.OpenRead(result.OutboxPath!);
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
        using var manifest = await ReadJsonAsync(manifestEntry);
        Assert.Contains(
            "ThrowingAnnotationHook",
            manifest.RootElement.GetProperty("hookFailures")[0].GetString());
    }

    [Fact]
    public async Task OfflineCaptureSink_OnlyReceivesAnnotationsFromItsRuntime()
    {
        using var annotationDirectory = new TemporaryAnnotationDirectory();
        using var offlineDirectory = new TemporaryAnnotationDirectory();
        var annotationRuntime = new RuntimeImpl(Options.CreateBuilder().Build());
        var offlineRuntime = new RuntimeImpl(Options.CreateBuilder().Build());
        await using var controller = new OfflineCaptureController(offlineRuntime, new OfflineCaptureOptions
        {
            RootDirectory = Path.Combine(offlineDirectory.Path, ".ansight"),
            SessionJpegCaptureEnabledOverride = false
        });
        var session = await controller.StartAsync();
        var service = new AnnotationService(
            annotationRuntime,
            new AnnotationOptionsBuilder()
                .WithoutScreenshot()
                .WithoutVisualTrees()
                .WithOutboxDirectory(annotationDirectory.Path)
                .Build());

        var result = await service.CaptureAsync(new AnnotationCaptureRequest(), CancellationToken.None);
        await controller.StopAsync();

        Assert.Equal(AnnotationCaptureStatus.Queued, result.Status);
        Assert.Empty(Directory.GetFiles(
            Path.Combine(session.DirectoryPath, "annotations", "bundles"),
            "*.ansightannotation"));
    }

    [Fact]
    public async Task GetVisualTreeTool_RoutesRequestsThroughRegisteredSource()
    {
        using var registration = VisualTreeProviderRegistry.Register(new StubVisualTreeProvider("custom"));

        var result = await new GetVisualTreeTool().Execute(new Dictionary<string, string>
        {
            ["source"] = "CUSTOM"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("custom", result.Payload?["source"]?.GetValue<string>());
        Assert.Contains("custom", VisualTreeProviderRegistry.GetRegisteredSources());
    }

    [Fact]
    public void WithAnnotatedFeedback_IsExplicitAndNotPartOfAggregateDefaults()
    {
        var defaultOptions = Options.CreateBuilder().WithAnsightSdk().Build();
        var defaultMauiOptions = Options.CreateBuilder().WithAnsightMaui().Build();
        var annotationOptions = Options.CreateBuilder().WithAnsightSdk().WithAnnotatedFeedback().Build();

        Assert.DoesNotContain(defaultOptions.RuntimeFeatures, feature => feature.Id == AnnotationRuntimeFeature.FeatureId);
        Assert.DoesNotContain(defaultMauiOptions.RuntimeFeatures, feature => feature.Id == AnnotationRuntimeFeature.FeatureId);
        Assert.Contains(annotationOptions.RuntimeFeatures, feature => feature.Id == AnnotationRuntimeFeature.FeatureId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildPolicy_UsesConsumerDebugBuildMetadata(bool expected)
    {
        var assemblyName = new AssemblyName($"AnnotationBuildPolicy{expected}{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]);
        Assert.NotNull(constructor);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            constructor,
            ["Ansight.Annotations.DebugBuild", expected.ToString()]));

        Assert.Equal(expected, AnnotationBuildPolicy.IsDebugBuild(assembly));
    }

    private static async Task<JsonDocument> ReadJsonAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed class StubVisualTreeProvider(string source) : IVisualTreeProvider
    {
        public string Source => source;

        public string DisplayName => "Stub tree";

        public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
        {
            return Task.FromResult(ToolResult.Success(new JsonObject
            {
                ["format"] = "test.visual-tree.compact.v2",
                ["source"] = source,
                ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["root"] = new JsonObject
                {
                    ["id"] = "root",
                    ["type"] = "StubRoot"
                },
                ["truncated"] = false
            }));
        }

        public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
            => Task.FromResult(ToolResult.Failure("Not implemented."));
    }

    private sealed class StubAnnotationHook : IAnnotationCaptureHook
    {
        public ValueTask ContributeAsync(AnnotationCaptureContext context, CancellationToken cancellationToken)
        {
            context.AddCustomData("flow", JsonValue.Create("checkout"));
            context.AddArtifact(new AnnotationArtifact(
                "Diagnostics",
                "text/plain",
                "diagnostics.txt",
                ArtifactPayload.FromText("ready")));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAnnotationHook : IAnnotationCaptureHook
    {
        public ValueTask ContributeAsync(AnnotationCaptureContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Hook data is unavailable.");
    }

    private sealed class CaptureTimestampHook : IAnnotationCaptureHook
    {
        internal DateTimeOffset? CapturedAtUtc { get; private set; }

        public ValueTask ContributeAsync(AnnotationCaptureContext context, CancellationToken cancellationToken)
        {
            CapturedAtUtc = context.CapturedAtUtc;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderingVisualTreeProvider(DateTimeOffset capturedAtUtc) : IVisualTreeProvider
    {
        internal const string SourceName = "annotation-ordering";

        internal bool WasCaptured { get; private set; }

        public string Source => SourceName;

        public string DisplayName => "Annotation ordering tree";

        public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
        {
            WasCaptured = true;
            return Task.FromResult(ToolResult.Success(new JsonObject
            {
                ["format"] = "test.visual-tree.compact.v2",
                ["source"] = SourceName,
                ["capturedAtUtc"] = capturedAtUtc.ToString("O"),
                ["root"] = new JsonObject
                {
                    ["id"] = "root",
                    ["type"] = "OrderingRoot"
                },
                ["truncated"] = false
            }));
        }

        public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
            => Task.FromResult(ToolResult.Failure("Not implemented."));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class DenyVisualTreePolicy : IAnnotationEvidencePolicy
    {
        public AnnotationEvidenceDecision Evaluate(AnnotationEvidenceDescriptor evidence)
            => evidence.Kind == AnnotationEvidenceKind.VisualTree
                ? AnnotationEvidenceDecision.Deny("Visual tree access is disabled by the app.")
                : AnnotationEvidenceDecision.Permit;
    }

    private sealed class TemporaryAnnotationDirectory : IDisposable
    {
        internal TemporaryAnnotationDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ansight-annotations-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
