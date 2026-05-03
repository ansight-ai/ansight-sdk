namespace Ansight;

/// <summary>
/// Identifies the connection flow the runtime-owned host connection should perform.
/// </summary>
public enum HostConnectionRequestKind
{
    /// <summary>
    /// Connect automatically using the runtime-cached session first, then saved and bundled configs.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Connect using the saved config store.
    /// </summary>
    SavedConfig = 1,

    /// <summary>
    /// Connect using the bundled config embedded in or supplied to the app.
    /// </summary>
    BundledConfig = 2,

    /// <summary>
    /// Read a pairing payload from a user-selected file or equivalent document source.
    /// </summary>
    File = 3,

    /// <summary>
    /// Read a pairing payload by scanning a QR code.
    /// </summary>
    QrCode = 4,

    /// <summary>
    /// Connect using a supplied config payload or compact config code.
    /// </summary>
    Payload = 5,

    /// <summary>
    /// Connect using a validated config document instance.
    /// </summary>
    Config = 6
}
