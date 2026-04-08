using Ansight.Pairing.Models;

namespace Ansight;

/// <summary>
/// Describes a requested Studio connection flow.
/// </summary>
public sealed record StudioConnectionRequest(
    StudioConnectionRequestKind Kind = StudioConnectionRequestKind.Auto,
    PairingTicket? Ticket = null,
    string? Payload = null,
    string? SourceDescription = null,
    string? Title = null)
{
    public static StudioConnectionRequest Auto(string? sourceDescription = null)
        => new(StudioConnectionRequestKind.Auto, SourceDescription: sourceDescription);

    public static StudioConnectionRequest SavedTicket(string? sourceDescription = null)
        => new(StudioConnectionRequestKind.SavedTicket, SourceDescription: sourceDescription);

    public static StudioConnectionRequest BundledTicket(string? sourceDescription = null)
        => new(StudioConnectionRequestKind.BundledTicket, SourceDescription: sourceDescription);

    public static StudioConnectionRequest File(string? title = null, string? sourceDescription = null)
        => new(StudioConnectionRequestKind.File, SourceDescription: sourceDescription, Title: title);

    public static StudioConnectionRequest QrCode(string? title = null, string? sourceDescription = null)
        => new(StudioConnectionRequestKind.QrCode, SourceDescription: sourceDescription, Title: title);

    public static StudioConnectionRequest PayloadText(string payload, string? sourceDescription = null)
        => new(StudioConnectionRequestKind.Payload, Payload: payload, SourceDescription: sourceDescription);

    public static StudioConnectionRequest TicketValue(PairingTicket ticket, string? sourceDescription = null)
        => new(StudioConnectionRequestKind.Ticket, Ticket: ticket, SourceDescription: sourceDescription);
}
