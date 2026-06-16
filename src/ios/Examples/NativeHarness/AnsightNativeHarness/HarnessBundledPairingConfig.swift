import Ansight
import Foundation

enum HarnessBundledPairingConfig {
    static var json: String? {
        if let embedded = AnsightDeveloperMode.embeddedPairingJson {
            return embedded
        }

        guard let url = Bundle.main.url(forResource: "ansight", withExtension: "json") else {
            return nil
        }

        return try? String(contentsOf: url, encoding: .utf8)
    }

    static var sourceDescription: String {
        if AnsightDeveloperMode.embeddedPairingJson != nil {
            return "build-plugin"
        }

        if Bundle.main.url(forResource: "ansight", withExtension: "json") != nil {
            return "app-resource"
        }

        return "<none>"
    }

    static var hasHostDiscovery: Bool {
        guard let json,
              let document = try? PairingConfigDocumentService().parseDocument(json)
        else {
            return false
        }

        return document.discoveryHint?.hostAddress != nil
    }
}
