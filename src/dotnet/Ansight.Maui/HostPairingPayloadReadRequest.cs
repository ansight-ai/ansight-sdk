namespace Ansight.Maui;

/// <summary>
/// Describes a MAUI host pairing payload read request.
/// </summary>
public sealed record HostPairingPayloadReadRequest(
    HostPairingPayloadReadKind Kind,
    string SourceDescription,
    string? Title = null);
