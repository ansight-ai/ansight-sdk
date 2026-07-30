import Foundation
import Network

public enum PairingFailureCodes {
    public static let hostAddressRequired = "HostAddressRequired"
    public static let wifiRequired = "WifiRequired"
    public static let enrollmentRequired = "EnrollmentRequired"
    public static let enrollmentUnavailable = "EnrollmentUnavailable"
    public static let enrollmentExpired = "EnrollmentExpired"
    public static let enrollmentConsumed = "EnrollmentConsumed"
    public static let accessTokenInvalid = "AccessTokenInvalid"
    public static let registrationExpired = "RegistrationExpired"
    public static let signInRequired = "SignInRequired"
    public static let udpBootstrapFailed = "UdpBootstrapFailed"
    public static let udpBootstrapTimeout = "UdpBootstrapTimeout"
    public static let webSocketHandoffUnavailable = "WebSocketHandoffUnavailable"
    public static let webSocketEndpointUnreachable = "WebSocketEndpointUnreachable"
    public static let webSocketHandshakeFailed = "WebSocketHandshakeFailed"
    public static let hostIdentityMismatch = "HostIdentityMismatch"
    public static let noSavedConfig = "NoSavedConfig"
    public static let noBundledConfig = "NoBundledConfig"
    public static let unsupportedSource = "UnsupportedSource"
}
