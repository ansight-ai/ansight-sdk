using System.Diagnostics;
using System.Text.Json.Nodes;
using Ansight.Telemetry.Events;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Rendering;
using Avalonia.Threading;

namespace Ansight.Tools.Avalonia;

internal sealed class AvaloniaRenderingDiagnostics
{
    private readonly AvaloniaInspectionSession session;

    internal AvaloniaRenderingDiagnostics(AvaloniaInspectionSession session)
    {
        this.session = session;
    }

    internal ToolResult Capture(IReadOnlyDictionary<string, string> arguments)
    {
        var roots = new JsonArray();

        foreach (var root in session.GetRoots())
        {
            var nodes = AvaloniaInspectionSession.Enumerate(root, AvaloniaTreeKind.Visual).Take(10_001).ToArray();
            var layouts = nodes.Take(10_000).OfType<Layoutable>().ToArray();

            roots.Add((JsonNode)new JsonObject
            {
                ["rootId"] = session.GetNodeId(root),
                ["type"] = root.GetType().FullName,
                ["clientSize"] = AvaloniaValueFormatter.Value(root.ClientSize),
                ["renderScaling"] = root.RenderScaling,
                ["rendererOverlays"] = root.RendererDiagnostics.DebugOverlays.ToString(),
                ["visualNodeCount"] = Math.Min(nodes.Length, 10_000),
                ["nodeCountTruncated"] = nodes.Length > 10_000,
                ["invalidMeasureCount"] = layouts.Count(layout => !layout.IsMeasureValid),
                ["invalidArrangeCount"] = layouts.Count(layout => !layout.IsArrangeValid),
                ["visible"] = root.IsEffectivelyVisible
            });
        }

        if (roots.Count == 0)
        {
            return ToolResult.Failure("No Avalonia rendering root is attached.", "avalonia_root_unavailable");
        }

        return ToolResult.Success(new JsonObject
        {
            ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["avaloniaVersion"] = typeof(TopLevel).Assembly.GetName().Version?.ToString(),
            ["roots"] = roots,
            ["gpuTimingAvailable"] = false,
            ["metrics"] = AvaloniaRenderingMetrics.Current?.Describe(),
            ["timingLimitations"] = "Numeric CPU render, update, layout and input timings are captured from Avalonia.Diagnostic.Meter. Render FPS counts completed passes across all targets; GPU presentation timing is not measured."
        });
    }

    internal ToolResult SetOverlays(IReadOnlyDictionary<string, string> arguments)
    {
        var root = ResolveRoot(arguments);
        var overlays = RendererDebugOverlays.None;

        foreach (var name in AvaloniaToolArguments.Required(arguments, "overlays").Split(',', StringSplitOptions.TrimEntries))
        {
            var isKnownName = Enum.GetNames<RendererDebugOverlays>()
                .Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (!isKnownName)
            {
                throw new ArgumentException("Overlays must be comma-separated names: None, Fps, DirtyRects, LayoutTimeGraph, RenderTimeGraph.");
            }

            overlays |= Enum.Parse<RendererDebugOverlays>(name, true);
        }

        var previous = root.RendererDiagnostics.DebugOverlays;
        root.RendererDiagnostics.DebugOverlays = overlays;

        return ToolResult.Success(new JsonObject
        {
            ["rootId"] = session.GetNodeId(root),
            ["previousOverlays"] = previous.ToString(),
            ["overlays"] = overlays.ToString()
        });
    }

    internal async Task<ToolResult> SampleAsync(IReadOnlyDictionary<string, string> arguments)
    {
        FrameSample? sample = null;
        var preparation = await AvaloniaInspectionSession.OnUiThreadAsync(() =>
        {
            var duration = AvaloniaToolArguments.Integer(arguments, "durationMilliseconds", 1000, 100, 5000);
            sample = new FrameSample(ResolveRoot(arguments), duration);
            sample.Start();

            return ToolResult.Success();
        });

        if (!preparation.IsSuccess)
        {
            return preparation;
        }

        var activeSample = sample!;

        // A bounded wall-clock window also completes when a backgrounded root stops producing callbacks.
        await Task.Delay(activeSample.DurationMilliseconds);

        return await AvaloniaInspectionSession.OnUiThreadAsync(() =>
        {
            var result = activeSample.Stop(session.GetNodeId(activeSample.Root));

            if (Runtime.IsInitialized && Runtime.IsActive)
            {
                Runtime.Event("Avalonia rendering sample", AppEventType.Info, result.ToJsonString());
            }

            return ToolResult.Success(result);
        });
    }

    private TopLevel ResolveRoot(IReadOnlyDictionary<string, string> arguments)
    {
        var rootId = AvaloniaToolArguments.Get(arguments, "rootId");
        var roots = session.GetRoots();

        if (rootId is not null)
        {
            return roots.FirstOrDefault(root => session.GetNodeId(root) == rootId)
                ?? throw new KeyNotFoundException("The rendering root is no longer attached.");
        }

        if (roots.Count != 1)
        {
            throw new ArgumentException("Specify rootId when the application has zero or multiple rendering roots.");
        }

        return roots[0];
    }

    private sealed class FrameSample
    {
        private readonly Stopwatch clock = new();
        private readonly List<double> intervals = [];
        private double? previousCallbackMilliseconds;
        private int callbackCount;
        private int layoutUpdateCount;
        private bool running;

        internal FrameSample(TopLevel root, int durationMilliseconds)
        {
            Root = root;
            DurationMilliseconds = durationMilliseconds;
        }

        internal TopLevel Root { get; }
        internal int DurationMilliseconds { get; }

        internal void Start()
        {
            running = true;
            Root.LayoutUpdated += LayoutUpdated;
            clock.Start();
            Root.RequestAnimationFrame(Frame);
        }

        internal JsonObject Stop(string rootId)
        {
            running = false;
            clock.Stop();
            Root.LayoutUpdated -= LayoutUpdated;
            intervals.Sort();

            return new JsonObject
            {
                ["rootId"] = rootId,
                ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["measurement"] = "animation-callback-cadence",
                ["isGpuPresentationRate"] = false,
                ["elapsedMilliseconds"] = clock.Elapsed.TotalMilliseconds,
                ["callbackCount"] = callbackCount,
                ["callbacksPerSecond"] = callbackCount / Math.Max(clock.Elapsed.TotalSeconds, 0.001),
                ["intervalCount"] = intervals.Count,
                ["meanIntervalMilliseconds"] = intervals.Count == 0 ? null : intervals.Average(),
                ["p95IntervalMilliseconds"] = intervals.Count == 0 ? null : intervals[(int)Math.Ceiling(intervals.Count * 0.95) - 1],
                ["maxIntervalMilliseconds"] = intervals.Count == 0 ? null : intervals[^1],
                ["layoutUpdateCount"] = layoutUpdateCount,
                ["hasSamples"] = callbackCount > 0
            };
        }

        private void Frame(TimeSpan timestamp)
        {
            if (!running)
            {
                return;
            }

            var now = clock.Elapsed.TotalMilliseconds;

            if (previousCallbackMilliseconds is { } previous)
            {
                intervals.Add(now - previous);
            }

            previousCallbackMilliseconds = now;
            callbackCount++;
            Root.RequestAnimationFrame(Frame);
        }

        private void LayoutUpdated(object? sender, EventArgs eventArgs)
        {
            layoutUpdateCount++;
        }
    }
}
