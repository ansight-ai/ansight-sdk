namespace Ansight.Native;

internal sealed record NativeTelemetrySnapshot(
    IReadOnlyList<NativeRecordedMetric> Metrics,
    IReadOnlyList<NativeRecordedEvent> Events);

internal sealed record NativeRecordedMetric(
    long Value,
    byte Channel,
    DateTime CapturedAtUtc,
    long Sequence);

internal sealed record NativeRecordedEvent(
    string Label,
    AppEventType Type,
    string Details,
    byte Channel,
    DateTime CapturedAtUtc,
    string? ExternalId,
    long Sequence);
