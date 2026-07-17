namespace Ansight.Annotations;

using System.Text;
using System.Text.Json.Nodes;
using Ansight.Screenshot;
using Ansight.Tools.VisualTree;

internal sealed class AnnotationEvidenceCapture
{
    private readonly AnnotationOptions options;

    internal AnnotationEvidenceCapture(AnnotationOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<AnnotationEvidenceSnapshot> CaptureAsync(
        Guid captureGroupId,
        CancellationToken cancellationToken)
    {
        var evidence = new List<AnnotationEvidenceResult>();
        AnnotationScreenshotSnapshot? screenshot = null;
        var visualTrees = new List<AnnotationVisualTreeSnapshot>();

        if (options.CaptureScreenshot)
        {
            var screenshotCapture = await CaptureScreenshotAsync(cancellationToken);
            evidence.Add(screenshotCapture.Result);
            screenshot = screenshotCapture.Screenshot;
        }
        else
        {
            evidence.Add(new AnnotationEvidenceResult(
                "screenshot",
                AnnotationEvidenceKind.Screenshot,
                AnnotationEvidenceStatus.Skipped,
                "Screenshot capture is disabled by annotation options."));
        }

        if (options.CaptureVisualTrees)
        {
            foreach (var provider in VisualTreeProviderRegistry.GetProviders())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var treeCapture = await CaptureVisualTreeAsync(provider, captureGroupId, cancellationToken);
                evidence.Add(treeCapture.Result);
                if (treeCapture.VisualTree is not null)
                {
                    visualTrees.Add(treeCapture.VisualTree);
                }
            }
        }

        return new AnnotationEvidenceSnapshot(captureGroupId, screenshot, visualTrees, evidence);
    }

    private async Task<AnnotationScreenshotCapture> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        var descriptor = new AnnotationEvidenceDescriptor(
            "screenshot",
            AnnotationEvidenceKind.Screenshot,
            "App screenshot");
        var decision = EvaluatePolicy(descriptor);
        if (!decision.IsPermitted)
        {
            return new AnnotationScreenshotCapture(
                null,
                new AnnotationEvidenceResult(
                    descriptor.Id,
                    descriptor.Kind,
                    AnnotationEvidenceStatus.NotPermitted,
                    decision.Reason ?? "Screenshot capture was denied by the host app."));
        }

        try
        {
            var captureOptions = new SessionJpegCaptureOptions
            {
                IntervalMilliseconds = 1000,
                Quality = options.ScreenshotQuality,
                MaxWidth = options.ScreenshotMaxWidth
            };
            using var frame = await SessionJpegCaptureSupport.CaptureJpegFrameAsync(captureOptions, cancellationToken);
            if (frame is null || frame.JpegPayload.IsEmpty)
            {
                return new AnnotationScreenshotCapture(
                    null,
                    new AnnotationEvidenceResult(
                        descriptor.Id,
                        descriptor.Kind,
                        AnnotationEvidenceStatus.Unavailable,
                        "No capturable app surface is available."));
            }

            var bytes = frame.JpegPayload.ToArray();
            var snapshot = new AnnotationScreenshotSnapshot(
                bytes,
                "image/jpeg",
                frame.Width,
                frame.Height,
                frame.CapturedAtUtc);
            return new AnnotationScreenshotCapture(
                snapshot,
                new AnnotationEvidenceResult(
                    descriptor.Id,
                    descriptor.Kind,
                    AnnotationEvidenceStatus.Captured,
                    CapturedAtUtc: frame.CapturedAtUtc,
                    SizeBytes: bytes.LongLength));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AnnotationScreenshotCapture(
                null,
                new AnnotationEvidenceResult(
                    descriptor.Id,
                    descriptor.Kind,
                    AnnotationEvidenceStatus.TimedOut,
                    "Screenshot capture timed out."));
        }
        catch (Exception exception)
        {
            return new AnnotationScreenshotCapture(
                null,
                new AnnotationEvidenceResult(
                    descriptor.Id,
                    descriptor.Kind,
                    AnnotationEvidenceStatus.Failed,
                    exception.Message));
        }
    }

    private async Task<AnnotationVisualTreeCapture> CaptureVisualTreeAsync(
        IVisualTreeProvider provider,
        Guid captureGroupId,
        CancellationToken cancellationToken)
    {
        var source = VisualTreeProviderRegistry.NormalizeSource(provider.Source);
        var evidenceId = $"visual-tree:{source}";
        var descriptor = new AnnotationEvidenceDescriptor(
            evidenceId,
            AnnotationEvidenceKind.VisualTree,
            provider.DisplayName);
        var decision = EvaluatePolicy(descriptor);
        if (!decision.IsPermitted)
        {
            return new AnnotationVisualTreeCapture(
                null,
                new AnnotationEvidenceResult(
                    evidenceId,
                    AnnotationEvidenceKind.VisualTree,
                    AnnotationEvidenceStatus.NotPermitted,
                    decision.Reason ?? "Visual-tree capture was denied by the host app."));
        }

        var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = source,
            ["includeBounds"] = "true",
            ["includeComputedStyles"] = options.IncludeVisualTreeProperties.ToString(),
            ["includeProperties"] = options.IncludeVisualTreeProperties.ToString(),
            ["includeBindableProperties"] = "false",
            ["includeBindingContexts"] = "false",
            ["includeInactivePages"] = "false",
            ["maxDepth"] = options.VisualTreeMaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxNodes"] = options.VisualTreeMaxNodes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.VisualTreeProviderTimeout);
            var toolResult = await provider.GetVisualTreeAsync(arguments).WaitAsync(timeout.Token);
            if (!toolResult.IsSuccess || toolResult.Payload is null)
            {
                var status = toolResult.ErrorCode is "visual_tree_platform_unsupported" or "maui_platform_unsupported" or "visual_tree_unavailable" or "maui_visual_tree_unavailable"
                    ? AnnotationEvidenceStatus.Unavailable
                    : AnnotationEvidenceStatus.Failed;
                return new AnnotationVisualTreeCapture(
                    null,
                    new AnnotationEvidenceResult(
                        evidenceId,
                        AnnotationEvidenceKind.VisualTree,
                        status,
                        toolResult.Message ?? toolResult.ErrorCode));
            }

            var payload = toolResult.Payload.DeepClone();
            if (payload is JsonObject payloadObject)
            {
                payloadObject["captureGroupId"] = captureGroupId.ToString("N");
            }

            var json = payload.ToJsonString();
            var byteCount = Encoding.UTF8.GetByteCount(json);
            if (byteCount > options.MaximumVisualTreeBytes)
            {
                return new AnnotationVisualTreeCapture(
                    null,
                    new AnnotationEvidenceResult(
                        evidenceId,
                        AnnotationEvidenceKind.VisualTree,
                        AnnotationEvidenceStatus.Failed,
                        $"Visual tree exceeded the {options.MaximumVisualTreeBytes:N0}-byte capture limit.",
                        SizeBytes: byteCount));
            }

            var capturedAtUtc = TryReadCapturedAt(payload) ?? DateTimeOffset.UtcNow;
            var truncated = payload["truncated"]?.GetValue<bool>() == true;
            var snapshot = new AnnotationVisualTreeSnapshot(source, provider.DisplayName, json, capturedAtUtc, truncated);
            return new AnnotationVisualTreeCapture(
                snapshot,
                new AnnotationEvidenceResult(
                    evidenceId,
                    AnnotationEvidenceKind.VisualTree,
                    AnnotationEvidenceStatus.Captured,
                    CapturedAtUtc: capturedAtUtc,
                    SizeBytes: byteCount,
                    Truncated: truncated));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AnnotationVisualTreeCapture(
                null,
                new AnnotationEvidenceResult(
                    evidenceId,
                    AnnotationEvidenceKind.VisualTree,
                    AnnotationEvidenceStatus.TimedOut,
                    $"Visual-tree provider '{source}' exceeded its capture timeout."));
        }
        catch (Exception exception)
        {
            return new AnnotationVisualTreeCapture(
                null,
                new AnnotationEvidenceResult(
                    evidenceId,
                    AnnotationEvidenceKind.VisualTree,
                    AnnotationEvidenceStatus.Failed,
                    exception.Message));
        }
    }

    private AnnotationEvidenceDecision EvaluatePolicy(AnnotationEvidenceDescriptor descriptor)
    {
        try
        {
            return options.EvidencePolicy.Evaluate(descriptor)
                   ?? AnnotationEvidenceDecision.Deny("The evidence policy returned no decision.");
        }
        catch (Exception exception)
        {
            return AnnotationEvidenceDecision.Deny($"The evidence policy failed: {exception.Message}");
        }
    }

    private static DateTimeOffset? TryReadCapturedAt(JsonNode payload)
    {
        var rawValue = payload["capturedAtUtc"]?.GetValue<string>();
        return DateTimeOffset.TryParse(rawValue, out var capturedAtUtc)
            ? capturedAtUtc.ToUniversalTime()
            : null;
    }
}

internal sealed record AnnotationEvidenceSnapshot(
    Guid CaptureGroupId,
    AnnotationScreenshotSnapshot? Screenshot,
    IReadOnlyList<AnnotationVisualTreeSnapshot> VisualTrees,
    IReadOnlyList<AnnotationEvidenceResult> Results);

internal sealed record AnnotationScreenshotSnapshot(
    byte[] Bytes,
    string MimeType,
    int Width,
    int Height,
    DateTimeOffset CapturedAtUtc);

internal sealed record AnnotationVisualTreeSnapshot(
    string Source,
    string DisplayName,
    string Json,
    DateTimeOffset CapturedAtUtc,
    bool Truncated);

internal sealed record AnnotationScreenshotCapture(
    AnnotationScreenshotSnapshot? Screenshot,
    AnnotationEvidenceResult Result);

internal sealed record AnnotationVisualTreeCapture(
    AnnotationVisualTreeSnapshot? VisualTree,
    AnnotationEvidenceResult Result);
