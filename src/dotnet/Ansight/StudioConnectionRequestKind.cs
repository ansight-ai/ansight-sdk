namespace Ansight;

/// <summary>
/// Identifies the connection flow the runtime-owned Studio connection should perform.
/// </summary>
public enum StudioConnectionRequestKind
{
    /// <summary>
    /// Connect automatically using the runtime-cached session first, then saved and bundled tickets.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Connect using the saved ticket store.
    /// </summary>
    SavedTicket = 1,

    /// <summary>
    /// Connect using the bundled ticket embedded in or supplied to the app.
    /// </summary>
    BundledTicket = 2,

    /// <summary>
    /// Read a pairing payload from a user-selected file or equivalent document source.
    /// </summary>
    File = 3,

    /// <summary>
    /// Read a pairing payload by scanning a QR code.
    /// </summary>
    QrCode = 4,

    /// <summary>
    /// Connect using a supplied ticket payload or compact ticket code.
    /// </summary>
    Payload = 5,

    /// <summary>
    /// Connect using a validated ticket instance.
    /// </summary>
    Ticket = 6
}
