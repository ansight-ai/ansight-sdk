#if !ANDROID && !IOS && !MACCATALYST
using System.Diagnostics;
using System.Net.NetworkInformation;
using Ansight.Telemetry.Events;

namespace Ansight.Connectivity;

/// <summary>Portable availability only; interface enumeration cannot identify the default route.</summary>
internal sealed class DesktopConnectivityMonitor(IRuntime runtime)
{
    private readonly object gate = new();
    private readonly string observationId = Guid.NewGuid().ToString();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool running;
    private long sequence;
    private ConnectivitySnapshot? previous;

    public void Start()
    {
        lock (gate)
        {
            if (running) return;
            running = true;
            NetworkChange.NetworkAvailabilityChanged += Changed;
            Observe();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (!running) return;
            NetworkChange.NetworkAvailabilityChanged -= Changed;
            Emit("gap", new(), "capture-stopped");
            previous = null;
            running = false;
        }
    }

    private void Changed(object? sender, NetworkAvailabilityEventArgs args)
    {
        lock (gate) { if (running) Observe(); }
    }

    private void Observe()
    {
        string availability;
        try { availability = NetworkInterface.GetIsNetworkAvailable() ? "available" : "unavailable"; }
        catch (NetworkInformationException) { availability = "unknown"; }
        var snapshot = new ConnectivitySnapshot { Availability = availability };
        if (previous?.Availability == availability) return;
        Emit(previous is null ? "snapshot" : "change", snapshot, null);
        previous = snapshot;
    }

    private void Emit(string kind, ConnectivitySnapshot snapshot, string? reason)
    {
        var record = new ConnectivityRecord
        {
            Id = Guid.NewGuid().ToString(), ObservationId = observationId, Sequence = ++sequence,
            CapturedAtUtc = DateTimeOffset.UtcNow, ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds,
            Source = "dotnet.network-information", Kind = kind, Snapshot = snapshot,
            PreviousSnapshot = kind == "change" ? previous : null, Reason = reason,
            SupportedMeasurements = ["availability"]
        };
        runtime.Event($"connectivity.{kind}", AppEventType.Info, ConnectivityJson.Serialize(record));
    }
}
#endif
