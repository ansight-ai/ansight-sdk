namespace Ansight.Pairing;

/// <summary>
/// Canonical machine-readable failure and rejection codes surfaced by the Ansight pairing APIs.
/// </summary>
public static class PairingFailureCodes
{
    /// <summary>
    /// A manual connection attempt did not supply a host address.
    /// </summary>
    public const string HostAddressRequired = "HostAddressRequired";

    /// <summary>
    /// The device is not connected to Wi-Fi, so a live pairing session cannot be opened.
    /// </summary>
    public const string WifiRequired = "WifiRequired";

    /// <summary>
    /// The host requires a current Studio enrollment invite.
    /// </summary>
    public const string EnrollmentRequired = "EnrollmentRequired";

    /// <summary>
    /// Enrollment is temporarily unavailable on the host.
    /// </summary>
    public const string EnrollmentUnavailable = "EnrollmentUnavailable";

    /// <summary>
    /// The one-use enrollment invite has expired.
    /// </summary>
    public const string EnrollmentExpired = "EnrollmentExpired";

    /// <summary>
    /// The one-use enrollment invite has already registered an installation.
    /// </summary>
    public const string EnrollmentConsumed = "EnrollmentConsumed";

    /// <summary>
    /// The enrollment or saved registration access token is invalid.
    /// </summary>
    public const string AccessTokenInvalid = "AccessTokenInvalid";

    /// <summary>
    /// The app installation's registration has expired or been revoked.
    /// </summary>
    public const string RegistrationExpired = "RegistrationExpired";

    /// <summary>
    /// The host requires the user to sign in to Ansight Studio before an app can connect.
    /// </summary>
    public const string SignInRequired = "SignInRequired";

    /// <summary>
    /// The UDP bootstrap handshake failed before the client could resolve a WebSocket endpoint.
    /// </summary>
    public const string UdpBootstrapFailed = "UdpBootstrapFailed";

    /// <summary>
    /// The UDP bootstrap handshake timed out before the client could resolve a WebSocket endpoint.
    /// </summary>
    public const string UdpBootstrapTimeout = "UdpBootstrapTimeout";

    /// <summary>
    /// The host accepted the request but did not return a usable WebSocket handoff.
    /// </summary>
    public const string WebSocketHandoffUnavailable = "WebSocketHandoffUnavailable";

    /// <summary>
    /// The client could not reach the advertised WebSocket endpoint.
    /// </summary>
    public const string WebSocketEndpointUnreachable = "WebSocketEndpointUnreachable";

    /// <summary>
    /// The WebSocket handshake failed after reaching the advertised endpoint.
    /// </summary>
    public const string WebSocketHandshakeFailed = "WebSocketHandshakeFailed";
}
