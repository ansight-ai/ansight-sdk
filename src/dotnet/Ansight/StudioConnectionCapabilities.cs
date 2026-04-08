namespace Ansight;

/// <summary>
/// Describes which runtime-owned Studio connection flows are currently available.
/// </summary>
public sealed record StudioConnectionCapabilities(
    bool CanConnectUsingSavedTicket,
    bool CanConnectUsingBundledTicket,
    bool CanChooseTicketFile,
    bool CanScanTicketQrCode,
    bool CanClearSavedTickets);
