namespace Ansight;

/// <summary>
/// Identifies a platform-defined pairing payload input surface.
/// </summary>
public enum HostPairingPayloadReadKind
{
    /// <summary>
    /// Read a pairing payload from a user-selected file or equivalent document source.
    /// </summary>
    File = 1,

    /// <summary>
    /// Read a pairing payload by scanning a QR code.
    /// </summary>
    QrCode = 2
}
