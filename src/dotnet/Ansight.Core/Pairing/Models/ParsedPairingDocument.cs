namespace Ansight.Pairing.Models;

/// <summary>
/// Parsed pairing payload that exposes the effective config plus any discovery metadata that accompanied it.
/// </summary>
public sealed class ParsedPairingDocument
{
    /// <summary>
    /// Effective pairing config to use for validation and connection.
    /// </summary>
    public PairingConfig? Config { get; init; }

    /// <summary>
    /// Effective protocol-v2 pairing config, when this is a secure document.
    /// </summary>
    public PairingConfigV2? SecureConfig { get; init; }

    /// <summary>
    /// Optional discovery metadata captured from the payload.
    /// </summary>
    public PairingDiscoveryHint? DiscoveryHint { get; init; }

    public bool IsSecureV2 => SecureConfig is not null;

    internal bool IsRememberedSecureV2 { get; init; }

    internal string ConfigId => SecureConfig?.ConfigId ?? Config?.ConfigId
        ?? throw new InvalidOperationException("Pairing document does not contain a config.");

    internal string AppId => SecureConfig?.AppId ?? Config?.AppId
        ?? throw new InvalidOperationException("Pairing document does not contain a config.");

    internal int HostDiscoveryPort => SecureConfig?.Host.DiscoveryPort ?? Config?.Host.DiscoveryPort
        ?? PairingProtocolDefaults.DiscoveryPort;

}
