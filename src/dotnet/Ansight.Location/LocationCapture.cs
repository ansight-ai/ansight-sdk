using Ansight.Pairing;

namespace Ansight.Location;

/// <summary>Explicit observed-location recording entry point.</summary>
public static class LocationCapture
{
    private static readonly Lock gate = new();
    private static LocationRecorder? recorder;

    public static bool IsEnabled
    {
        get
        {
            lock (gate)
            {
                return recorder is not null;
            }
        }
    }

    public static Task<OperationResult> RecordAsync(
        LocationSample sample,
        CancellationToken cancellationToken = default)
    {
        LocationRecorder? currentRecorder;
        lock (gate)
        {
            currentRecorder = recorder;
        }

        return currentRecorder is null
            ? Task.FromResult(OperationResult.FromFailure(
                "Observed location capture is disabled. Register WithObservedLocationCapture()."))
            : currentRecorder.RecordAsync(sample, cancellationToken);
    }

    internal static void Initialize(LocationRecorder locationRecorder)
    {
        lock (gate)
        {
            recorder = locationRecorder ?? throw new ArgumentNullException(nameof(locationRecorder));
        }
    }
}
