import CryptoKit
import Foundation

public enum PairingProtocolDefaults {
    public static let discoveryPort = 45123
    public static let developerDiscoveryPort = 46123
    public static let webSocketPort = 45124
    public static let webSocketPath = "/ws"

    static let localDiscoveryPorts = [
        discoveryPort,
        developerDiscoveryPort,
    ]
}
