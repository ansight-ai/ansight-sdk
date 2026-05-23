namespace Ansight.Pairing;

internal readonly record struct TouchCaptureBatchKey(
    string Space,
    string Unit,
    double? SurfaceWidth,
    double? SurfaceHeight,
    double? SurfaceScale);
