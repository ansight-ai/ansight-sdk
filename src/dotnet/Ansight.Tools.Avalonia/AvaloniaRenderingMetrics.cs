using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Text.Json.Nodes;
using Ansight.Telemetry.Channels;

namespace Ansight.Tools.Avalonia;

public sealed class AvaloniaRenderingMetrics : IDisposable
{
    private const string MeterName = "Avalonia.Diagnostic.Meter";
    private const byte FirstChannelId = 40;
    private const byte RenderFpsChannelId = 61;
    private readonly object gate = new();
    private readonly MeterListener listener;
    private readonly Action<double, byte> publish;
    private readonly Func<bool> isActive;
    private readonly Dictionary<string, MeasurementWindow> measurements;
    private readonly Stopwatch clock = new();
    private Timer? timer;
    private bool started;
    private bool disposed;

    public AvaloniaRenderingMetrics()
        : this(Runtime.Metric, () => Runtime.IsInitialized && Runtime.IsActive)
    {
    }

    internal AvaloniaRenderingMetrics(Action<double, byte> publish, Func<bool> isActive)
    {
        this.publish = publish;
        this.isActive = isActive;
        measurements = CreateMeasurements().ToDictionary(item => item.InstrumentName, StringComparer.Ordinal);
        listener = new MeterListener { InstrumentPublished = InstrumentPublished };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => Record(instrument, value));
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => Record(instrument, value));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => Record(instrument, value));
    }

    public static AvaloniaRenderingMetrics? Current { get; private set; }

    public IReadOnlyList<Channel> Channels
    {
        get
        {
            var channels = new List<Channel>();

            foreach (var measurement in measurements.Values)
            {
                if (measurement.IsDuration)
                {
                    channels.Add(CreateChannel(measurement.ChannelId, measurement.Label + " mean", "ms", "duration-mean"));
                    channels.Add(CreateChannel((byte)(measurement.ChannelId + 1), measurement.Label + " max", "ms", "duration-max"));
                    channels.Add(CreateChannel((byte)(measurement.ChannelId + 2), measurement.Label + " samples", "count", "sample-count"));
                }
                else
                {
                    channels.Add(CreateChannel(measurement.ChannelId, measurement.Label, "count", "gauge"));
                }
            }

            channels.Add(CreateChannel(RenderFpsChannelId, "Avalonia Render FPS (all targets)", "fps", "render-pass-rate"));
            return channels;
        }
    }

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (started)
            {
                return;
            }

            // Avalonia reads this switch once, before initializing its first diagnostic.
            AppContext.SetSwitch("Avalonia.Diagnostics.Diagnostic.IsEnabled", true);
            started = true;
            Current = this;
            listener.Start();
            clock.Start();
            timer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    public JsonObject Describe()
    {
        lock (gate)
        {
            var instruments = new JsonArray();

            foreach (var measurement in measurements.Values)
            {
                instruments.Add((JsonNode)new JsonObject
                {
                    ["instrument"] = measurement.InstrumentName,
                    ["available"] = measurement.Available,
                    ["receivedSamples"] = measurement.TotalCount,
                    ["channelId"] = measurement.ChannelId
                });
            }

            return new JsonObject
            {
                ["source"] = MeterName,
                ["enabled"] = started && !disposed,
                ["aggregationMilliseconds"] = 1000,
                ["instruments"] = instruments,
                ["renderFpsChannelId"] = RenderFpsChannelId,
                ["renderFpsScope"] = "Completed compositor render passes summed across all targets; idle targets produce zero. This is not GPU presentation FPS.",
                ["timingScope"] = "CPU-side Avalonia instrumentation, with mean, maximum and sample count per reporting window."
            };
        }
    }

    internal void Flush()
    {
        lock (gate)
        {
            if (disposed || !started)
            {
                return;
            }

            listener.RecordObservableInstruments();
            var seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.001);
            clock.Restart();
            var active = isActive();

            foreach (var measurement in measurements.Values)
            {
                if (active && measurement.Count > 0)
                {
                    if (measurement.IsDuration)
                    {
                        publish(measurement.Sum / measurement.Count, measurement.ChannelId);
                        publish(measurement.Maximum, (byte)(measurement.ChannelId + 1));
                        publish(measurement.Count, (byte)(measurement.ChannelId + 2));
                    }
                    else
                    {
                        publish(measurement.Latest, measurement.ChannelId);
                    }
                }

                var publishRenderRate = active
                    && measurement.Available
                    && measurement.InstrumentName == "avalonia.comp.render.time";

                if (publishRenderRate)
                {
                    publish(measurement.Count / seconds, RenderFpsChannelId);
                }

                measurement.Reset();
            }
        }
    }

    private void InstrumentPublished(Instrument instrument, MeterListener meterListener)
    {
        lock (gate)
        {
            var supported = instrument.Meter.Name == MeterName
                && measurements.TryGetValue(instrument.Name, out var measurement)
                && (!measurement.IsDuration || instrument.Unit == "ms");

            if (!supported)
            {
                return;
            }

            measurements[instrument.Name].Available = true;
            meterListener.EnableMeasurementEvents(instrument);
        }
    }

    private void Record(Instrument instrument, double value)
    {
        var valid = double.IsFinite(value)
            && value >= 0
            && isActive();

        if (!valid)
        {
            return;
        }

        lock (gate)
        {
            if (disposed || !measurements.TryGetValue(instrument.Name, out var measurement))
            {
                return;
            }

            measurement.Count++;
            measurement.TotalCount++;
            measurement.Sum += value;
            measurement.Maximum = Math.Max(measurement.Maximum, value);
            measurement.Latest = value;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            timer?.Dispose();

            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        listener.Dispose();
    }

    private static Channel CreateChannel(byte id, string name, string unit, string kind)
    {
        var group = id is >= 46 and <= 51 ? MetricGroups.Layout
            : id is >= 55 and <= 60 ? MetricGroups.Runtime
            : MetricGroups.Rendering;
        var color = id switch
        {
            >= 40 and <= 42 => Color.FromArgb(94, 92, 230),
            >= 43 and <= 45 => Color.FromArgb(0, 122, 255),
            >= 46 and <= 48 => Color.FromArgb(175, 82, 222),
            >= 49 and <= 51 => Color.FromArgb(255, 149, 0),
            >= 52 and <= 54 => Color.FromArgb(0, 168, 150),
            >= 55 and <= 57 => Color.FromArgb(255, 55, 95),
            58 => Color.FromArgb(172, 142, 104),
            59 => Color.FromArgb(90, 200, 250),
            60 => Color.FromArgb(255, 204, 0),
            _ => Color.FromArgb(35, 181, 115)
        };

        return new Channel(id, name, group, color, unit, "custom", "avalonia.diagnostics", kind);
    }

    private static IEnumerable<MeasurementWindow> CreateMeasurements()
    {
        yield return new MeasurementWindow("avalonia.comp.render.time", "Avalonia compositor render", FirstChannelId, true);
        yield return new MeasurementWindow("avalonia.comp.update.time", "Avalonia compositor update", 43, true);
        yield return new MeasurementWindow("avalonia.ui.measure.time", "Avalonia layout measure", 46, true);
        yield return new MeasurementWindow("avalonia.ui.arrange.time", "Avalonia layout arrange", 49, true);
        yield return new MeasurementWindow("avalonia.ui.render.time", "Avalonia render recording", 52, true);
        yield return new MeasurementWindow("avalonia.ui.input.time", "Avalonia input processing", 55, true);
        yield return new MeasurementWindow("avalonia.ui.event.handler.count", "Avalonia event handlers", 58, false);
        yield return new MeasurementWindow("avalonia.ui.visual.count", "Avalonia rooted visuals", 59, false);
        yield return new MeasurementWindow("avalonia.ui.dispatcher.timer.count", "Avalonia dispatcher timers", 60, false);
    }

    private sealed class MeasurementWindow
    {
        internal MeasurementWindow(string instrumentName, string label, byte channelId, bool isDuration)
        {
            InstrumentName = instrumentName;
            Label = label;
            ChannelId = channelId;
            IsDuration = isDuration;
        }

        internal string InstrumentName { get; }
        internal string Label { get; }
        internal byte ChannelId { get; }
        internal bool IsDuration { get; }
        internal bool Available { get; set; }
        internal long TotalCount { get; set; }
        internal long Count { get; set; }
        internal double Sum { get; set; }
        internal double Maximum { get; set; }
        internal double Latest { get; set; }

        internal void Reset()
        {
            Count = 0;
            Sum = 0;
            Maximum = 0;
            Latest = 0;
        }
    }
}
