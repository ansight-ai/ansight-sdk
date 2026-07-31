import Foundation

enum PairingSimulatorLocalHostAddress {
    static func resolve() -> String? {
        #if targetEnvironment(simulator) || targetEnvironment(macCatalyst) || os(macOS)
        return "127.0.0.1"
        #else
        return nil
        #endif
    }
}

enum PairingHostAddressCandidates {
    static func resolve(
        discoveryHint: PairingDiscoveryHint?,
        hostAddressOverride: String?,
        simulatorLocalHostAddress: String?
    ) -> [String] {
        if let hostAddressOverride = hostAddressOverride?.trimmingCharacters(in: .whitespacesAndNewlines),
           !hostAddressOverride.isEmpty {
            return [hostAddressOverride]
        }

        var addresses: [String?] = [simulatorLocalHostAddress]
        addresses.append(contentsOf: discoveryHint?.hostAddresses?.map { Optional($0) } ?? [])

        var seen = Set<String>()
        var candidates: [String] = []
        for address in addresses {
            guard let candidate = address?.trimmingCharacters(in: .whitespacesAndNewlines),
                  !candidate.isEmpty,
                  seen.insert(candidate.lowercased()).inserted
            else {
                continue
            }

            candidates.append(candidate)
        }

        return candidates
    }
}
