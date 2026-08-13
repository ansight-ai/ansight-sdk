using System.Text.Json.Nodes;
using Ansight.Input;
using Ansight.Pairing;

namespace Ansight.Screenshot;

internal sealed class PairingSessionJpegStreamer : IDisposable
{
    private readonly PairingSessionTransport transport;
    private CancellationTokenSource? captureCts;
    private Task? captureTask;
    private TouchVisualTreeCaptureCoordinator? touchVisualTreeCaptureCoordinator;
    private HostSessionJpegCapturePolicy hostCapturePolicy = HostSessionJpegCapturePolicy.App;
    private bool disposed;

    public PairingSessionJpegStreamer(PairingSessionTransport transport)
    {
        this.transport = transport;
    }

    public async Task StartAsync(IProgress<HostConnectionProgressUpdate>? progress)
    {
        await StopAsync(CancellationToken.None);

        if (hostCapturePolicy.UseHostCapture)
        {
            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.SessionJpegCapture,
                $"Session JPEG capture delegated to Studio ({hostCapturePolicy.Source ?? "external"}).",
                source: HostConnectionSource.SessionJpegCapture);
            return;
        }

        var options = ResolveOptions();
        if (options is null)
        {
            return;
        }

        captureCts = new CancellationTokenSource();
        captureTask = Task.Run(() => RunCapturePumpAsync(options, progress, captureCts.Token));
        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.SessionJpegCapture,
            $"Session JPEG capture started ({options.IntervalMilliseconds}ms, quality {options.Quality}, max width {(options.MaxWidth?.ToString() ?? "native")}).",
            source: HostConnectionSource.SessionJpegCapture);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var touchVisualTreeCaptureCoordinator = this.touchVisualTreeCaptureCoordinator;
        var captureCts = this.captureCts;
        var captureTask = this.captureTask;

        this.touchVisualTreeCaptureCoordinator = null;
        this.captureCts = null;
        this.captureTask = null;

        if (touchVisualTreeCaptureCoordinator is not null)
        {
            await touchVisualTreeCaptureCoordinator.StopAsync(cancellationToken);
            touchVisualTreeCaptureCoordinator.Dispose();
        }

        captureCts?.Cancel();

        if (captureTask is not null)
        {
            try
            {
                await captureTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch
            {
                // Ignore pump errors during shutdown.
            }
        }

        captureCts?.Dispose();
    }

    public void SetHostCapturePolicy(HostSessionJpegCapturePolicy policy)
    {
        hostCapturePolicy = policy ?? HostSessionJpegCapturePolicy.App;
    }

    public async Task StartTouchVisualTreeCaptureAsync(
        TouchCaptureHub touchCaptureHub,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(touchCaptureHub);

        var existingCoordinator = touchVisualTreeCaptureCoordinator;
        touchVisualTreeCaptureCoordinator = null;
        if (existingCoordinator is not null)
        {
            await existingCoordinator.StopAsync(cancellationToken);
            existingCoordinator.Dispose();
        }

        var options = ResolveOptions();
        if (!touchCaptureHub.IsEnabled
            || options?.Mode != SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch)
        {
            return;
        }

        var coordinator = new TouchVisualTreeCaptureCoordinator(CaptureAndSendTouchVisualTreesAsync);
        touchVisualTreeCaptureCoordinator = coordinator;
        coordinator.Start(touchCaptureHub);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        touchVisualTreeCaptureCoordinator?.Dispose();
        captureCts?.Cancel();
        captureCts?.Dispose();
        captureTask = null;
        captureCts = null;
        touchVisualTreeCaptureCoordinator = null;
    }

    private static SessionJpegCaptureOptions? ResolveOptions()
    {
        if (!Runtime.IsInitialized)
        {
            return null;
        }

        var configured = Runtime.MutableInstance.Options.SessionJpegCapture;
        if (configured is null)
        {
            return null;
        }

        return new SessionJpegCaptureOptions
        {
            IntervalMilliseconds = configured.IntervalMilliseconds,
            Quality = configured.Quality,
            MaxWidth = configured.MaxWidth,
            CaptureGpuBackedSurfaces = configured.CaptureGpuBackedSurfaces,
            Mode = configured.Mode
        };
    }

    private static bool ShouldSkipCaptureForLifecycle()
    {
        return Runtime.CurrentAppLifecycleState == AppLifecycleState.Background;
    }

    private async Task RunCapturePumpAsync(
        SessionJpegCaptureOptions options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(options.IntervalMilliseconds);
        var captureImmediately = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!captureImmediately)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            captureImmediately = false;

            try
            {
                if (ShouldSkipCaptureForLifecycle())
                {
                    continue;
                }

                var surface = await SessionJpegCaptureSupport.CaptureSurfaceAsync(options, cancellationToken);
                if (surface is null)
                {
                    continue;
                }

                using (surface)
                {
                    IReadOnlyList<JsonObject> visualTrees = Array.Empty<JsonObject>();
                    if (options.Mode == SessionJpegCaptureMode.ScreenshotAndVisualTree)
                    {
                        try
                        {
                            visualTrees = await SessionVisualTreeCaptureRegistry.CaptureAsync(cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning($"Session visual-tree capture skipped: {ex.Message}");
                        }
                    }

                    var sendResult = await SessionJpegCaptureSupport.SendSurfaceAsync(
                        surface,
                        options,
                        transport,
                        cancellationToken);
                    if (!sendResult.Success)
                    {
                        HostPairingProgressReporter.Report(
                            progress,
                            HostConnectionProgressKind.Warning,
                            $"Session JPEG capture stopped: {sendResult.Message}",
                            source: HostConnectionSource.SessionJpegCapture);
                        return;
                    }

                    foreach (var visualTree in visualTrees)
                    {
                        var visualTreeEvent = CreateVisualTreeEvent(
                            visualTree,
                            surface.CapturedAtUtc,
                            surface.CapturedAtUtc,
                            trigger: null);
                        sendResult = await transport.SendTextAsync(visualTreeEvent.ToJsonString(), cancellationToken);
                        if (!sendResult.Success)
                        {
                            HostPairingProgressReporter.Report(
                                progress,
                                HostConnectionProgressKind.Warning,
                                $"Session visual-tree capture skipped: {sendResult.Message}",
                                source: HostConnectionSource.SessionJpegCapture);
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Session JPEG capture skipped: {ex.Message}");
            }
        }
    }

    private async Task CaptureAndSendTouchVisualTreesAsync(
        TouchVisualTreeCaptureTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (ShouldSkipCaptureForLifecycle() || !transport.IsOpen)
        {
            return;
        }

        var capturedAtUtc = DateTimeOffset.UtcNow;
        var visualTrees = await SessionVisualTreeCaptureRegistry.CaptureAsync(cancellationToken);
        foreach (var visualTree in visualTrees)
        {
            var visualTreeEvent = CreateVisualTreeEvent(
                visualTree,
                capturedAtUtc,
                screenshotCapturedAtUtc: null,
                trigger);
            var result = await transport.SendTextAsync(visualTreeEvent.ToJsonString(), cancellationToken);
            if (!result.Success)
            {
                Logger.Warning($"Touch visual-tree capture skipped: {result.Message}");
                return;
            }
        }
    }

    private static JsonObject CreateVisualTreeEvent(
        JsonObject sourcePayload,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset? screenshotCapturedAtUtc,
        TouchVisualTreeCaptureTrigger? trigger)
    {
        var payload = sourcePayload.DeepClone() as JsonObject ?? new JsonObject();
        payload["capturedAtUtc"] = capturedAtUtc.ToUniversalTime().ToString("O");
        if (trigger is not null)
        {
            payload["captureTrigger"] = new JsonObject
            {
                ["kind"] = "touch",
                ["gestureId"] = trigger.GestureId,
                ["gesturePhase"] = SerializeGesturePhase(trigger.GesturePhase),
                ["touchAction"] = SerializeTouchAction(trigger.TouchAction),
                ["touchCapturedAtUtc"] = trigger.TouchCapturedAtUtc.ToUniversalTime()
            };
        }
        var source = ReadString(payload, "source") ?? "native";
        var format = ReadString(payload, "format") ?? ReadString(payload, "schema") ?? "ansight.visual-tree.compact.v2";
        var platform = ReadString(payload, "platform") ?? "dotnet";
        var nodeCount = ReadInt32(payload, "nodeCount") ?? CountVisualTreeNodes(payload);
        var truncated = ReadBoolean(payload, "truncated") ?? false;

        var result = new JsonObject
        {
            ["type"] = "CLIENT_VISUAL_TREE",
            ["snapshotId"] = $"stream-{Guid.NewGuid():N}",
            ["capturedAtUtc"] = capturedAtUtc.ToUniversalTime(),
            ["visualTreeKind"] = source,
            ["visualTreeFormat"] = format,
            ["runtimePlatform"] = platform,
            ["source"] = trigger is null ? "sdk.sessionCapture" : "sdk.touchCapture",
            ["maxDepth"] = 40,
            ["includeProperties"] = true,
            ["includeBindableProperties"] = false,
            ["nodeCount"] = Math.Max(0, nodeCount),
            ["truncated"] = truncated,
            ["payload"] = payload
        };

        if (screenshotCapturedAtUtc.HasValue)
        {
            result["screenshotCapturedAtUtc"] = screenshotCapturedAtUtc.Value.ToUniversalTime();
        }

        if (trigger is not null)
        {
            result["captureTrigger"] = "touch";
            result["gestureId"] = trigger.GestureId;
            result["gesturePhase"] = SerializeGesturePhase(trigger.GesturePhase);
            result["touchAction"] = SerializeTouchAction(trigger.TouchAction);
            result["touchCapturedAtUtc"] = trigger.TouchCapturedAtUtc.ToUniversalTime();
        }

        return result;
    }

    private static string SerializeGesturePhase(TouchVisualTreeGesturePhase phase)
        => phase.ToString().ToLowerInvariant();

    private static string SerializeTouchAction(CapturedTouchAction action)
        => action.ToString().ToLowerInvariant();

    private static string? ReadString(JsonObject payload, string propertyName)
        => payload[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : null;

    private static int? ReadInt32(JsonObject payload, string propertyName)
        => payload[propertyName] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : null;

    private static bool? ReadBoolean(JsonObject payload, string propertyName)
        => payload[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : null;

    private static int CountVisualTreeNodes(JsonObject payload)
    {
        if (payload["nodes"] is JsonArray nodes)
        {
            return nodes.Count;
        }

        return payload["root"] is JsonObject root ? CountVisualTreeNode(root) : 0;
    }

    private static int CountVisualTreeNode(JsonObject node)
    {
        if (node["children"] is not JsonArray children)
        {
            return 1;
        }

        var count = 1;
        foreach (var child in children.OfType<JsonObject>())
        {
            count += CountVisualTreeNode(child);
        }

        return count;
    }
}
