namespace Ansight.Pairing.Models;

/// <summary>
/// Enrollment connection request sent before a live WebSocket session is opened.
/// </summary>
public sealed class ConnectRequest
{
    /// <summary>
    /// Protocol message type identifier.
    /// </summary>
    public string Type { get; set; } = "ENROLLMENT_CONNECT";

    /// <summary>
    /// Handshake protocol version.
    /// </summary>
    public int Ver { get; set; } = 2;

    /// <summary>
    /// Correlates the UDP response with this request.
    /// </summary>
    public required string RequestId { get; set; }

    /// <summary>
    /// Enrollment mode requested by the runtime.
    /// </summary>
    public string EnrollmentMode { get; set; } = PairingEnrollmentModes.Invite;

    /// <summary>
    /// Unique identifier of the enrollment invite being used.
    /// </summary>
    public required string InviteId { get; set; }

    /// <summary>
    /// App identifier expected by the pairing config.
    /// </summary>
    public required string AppId { get; set; }

    /// <summary>
    /// Stable identifier generated once for this SDK installation.
    /// </summary>
    public required string DeviceId { get; set; }

    /// <summary>
    /// Human-readable name presented to Studio for this device.
    /// </summary>
    public required string DeviceName { get; set; }

    /// <summary>
    /// Opaque access token carried by the scanned QR.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Process-lifetime identifier for the SDK runtime instance.
    /// </summary>
    public string? ProcessSessionId { get; set; }

}
