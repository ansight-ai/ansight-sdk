import Foundation
import Network

public enum PairingFailureCodes {
    public static let hostAddressRequired = "HostAddressRequired"
    public static let wifiRequired = "WifiRequired"
    public static let pairingRequired = "PairingRequired"
    public static let pairingTokenInvalid = "PairingTokenInvalid"
    public static let pairingTokenExpired = "PairingTokenExpired"
    public static let pairingProofInvalid = "PairingProofInvalid"
    public static let signInRequired = "SignInRequired"
    public static let udpBootstrapFailed = "UdpBootstrapFailed"
    public static let udpBootstrapTimeout = "UdpBootstrapTimeout"
    public static let webSocketHandoffUnavailable = "WebSocketHandoffUnavailable"
    public static let webSocketEndpointUnreachable = "WebSocketEndpointUnreachable"
    public static let webSocketHandshakeFailed = "WebSocketHandshakeFailed"
    public static let transportNegotiationFailed = "TransportNegotiationFailed"
    public static let hostCertificateMismatch = "HostCertificateMismatch"
    public static let secureAuthenticationFailed = "SecureAuthenticationFailed"
    public static let hostIdentityMismatch = "HostIdentityMismatch"
    public static let noSavedConfig = "NoSavedConfig"
    public static let noBundledConfig = "NoBundledConfig"
    public static let unsupportedSource = "UnsupportedSource"
}
