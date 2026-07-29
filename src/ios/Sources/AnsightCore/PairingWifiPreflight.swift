import Foundation

#if canImport(Network)
import Network
#endif

enum PairingWifiPreflight {
    static func getStatus() -> PairingWifiPreflightStatus {
        #if canImport(Network)
        let path = currentNetworkPathSnapshot()
        guard path.status == .satisfied else {
            return .notConnected
        }

        if path.usesInterfaceType(.wifi) || path.usesInterfaceType(.wiredEthernet) {
            return .connected
        }

        if path.usesInterfaceType(.cellular) {
            return .cellular
        }

        return .unknown
        #else
        return .unknown
        #endif
    }

    #if canImport(Network)
    private static func currentNetworkPathSnapshot() -> NWPath {
        let monitor = NWPathMonitor()
        let queue = DispatchQueue(label: "ai.ansight.pairing.wifi-preflight")
        monitor.start(queue: queue)
        Thread.sleep(forTimeInterval: 0.05)
        monitor.cancel()
        return monitor.currentPath
    }
    #endif
}
