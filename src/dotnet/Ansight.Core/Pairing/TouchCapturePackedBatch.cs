namespace Ansight.Pairing;

internal sealed record TouchCapturePackedBatch(
    DateTimeOffset T0,
    string Space,
    string Unit,
    double?[] Surface,
    List<object?[]> Rows);
