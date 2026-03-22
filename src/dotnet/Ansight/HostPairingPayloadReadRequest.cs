namespace Ansight;

/// <summary>
/// Describes a requested pairing payload input flow.
/// </summary>
public sealed record HostPairingPayloadReadRequest(
    HostPairingPayloadReadKind Kind,
    string? SourceDescription = null,
    string? Title = null);
