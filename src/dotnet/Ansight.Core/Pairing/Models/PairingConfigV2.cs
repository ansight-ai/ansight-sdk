namespace Ansight.Pairing.Models;

/// <summary>
/// Signed protocol-v2 pairing configuration. Timestamp strings are retained
/// exactly because their original RFC 3339 representation is signature-bound.
/// </summary>
public sealed class PairingConfigV2
{
    public const string SchemaName = "ansight.pairing-config.v2";

    public required string Schema { get; set; }
    public required string ConfigId { get; set; }
    public required string AppId { get; set; }
    public required string AppName { get; set; }
    public required string IssuedAt { get; set; }
    public required string ExpiresAt { get; set; }
    public required int MinProtocolVersion { get; set; }
    public string[] AllowedTransports { get; set; } = [];
    public required PairingHostV2 Host { get; set; }
    public required PairingEnrollment Enrollment { get; set; }
    public required string SignatureAlgorithm { get; set; }
    public required string Signature { get; set; }
}

public sealed class PairingHostV2
{
    public required string HostId { get; set; }
    public required string HostName { get; set; }
    public int DiscoveryPort { get; set; } = PairingProtocolDefaults.DiscoveryPort;
    public required string HostPubKey { get; set; }
    public required string HostPubKeyFingerprint { get; set; }
    public PairingTlsPin[] TlsPins { get; set; } = [];
}

public sealed class PairingConfigDocumentV2
{
    public const string SchemaName = "ansight.pairing-config-document.v2";

    public string Schema { get; set; } = SchemaName;
    public required PairingConfigV2 Config { get; set; }
    public PairingDiscoveryHint? Discovery { get; set; }
}
