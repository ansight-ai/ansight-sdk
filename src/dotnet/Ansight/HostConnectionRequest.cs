using Ansight.Pairing.Models;

namespace Ansight;

/// <summary>
/// Describes a requested host connection flow.
/// </summary>
public sealed record HostConnectionRequest(
    HostConnectionRequestKind Kind = HostConnectionRequestKind.Auto,
    PairingConfigDocument? Config = null,
    string? Payload = null,
    string? SourceDescription = null,
    string? Title = null)
{
    public static HostConnectionRequest Auto(string? sourceDescription = null)
        => new(HostConnectionRequestKind.Auto, SourceDescription: sourceDescription);

    public static HostConnectionRequest SavedConfig(string? sourceDescription = null)
        => new(HostConnectionRequestKind.SavedConfig, SourceDescription: sourceDescription);

    public static HostConnectionRequest BundledConfig(string? sourceDescription = null)
        => new(HostConnectionRequestKind.BundledConfig, SourceDescription: sourceDescription);

    public static HostConnectionRequest File(string? title = null, string? sourceDescription = null)
        => new(HostConnectionRequestKind.File, SourceDescription: sourceDescription, Title: title);

    public static HostConnectionRequest QrCode(string? title = null, string? sourceDescription = null)
        => new(HostConnectionRequestKind.QrCode, SourceDescription: sourceDescription, Title: title);

    public static HostConnectionRequest PayloadText(string payload, string? sourceDescription = null)
        => new(HostConnectionRequestKind.Payload, Payload: payload, SourceDescription: sourceDescription);

    public static HostConnectionRequest ConfigValue(PairingConfigDocument config, string? sourceDescription = null)
        => new(HostConnectionRequestKind.Config, Config: config, SourceDescription: sourceDescription);
}
