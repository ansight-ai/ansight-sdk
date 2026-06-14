import Foundation

public struct HostConnectionRequest: Sendable {
    public let kind: HostConnectionRequestKind
    public let config: PairingConfigDocument?
    public let payload: String?
    public let sourceDescription: String?
    public let title: String?
    public let clientName: String?

    public init(
        kind: HostConnectionRequestKind = .auto,
        config: PairingConfigDocument? = nil,
        payload: String? = nil,
        sourceDescription: String? = nil,
        title: String? = nil,
        clientName: String? = nil
    ) {
        self.kind = kind
        self.config = config
        self.payload = payload
        self.sourceDescription = sourceDescription
        self.title = title
        self.clientName = clientName
    }

    public static func auto(clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .auto, sourceDescription: sourceDescription, clientName: clientName)
    }

    public static func savedConfig(clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .savedConfig, sourceDescription: sourceDescription, clientName: clientName)
    }

    public static func bundledConfig(clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .bundledConfig, sourceDescription: sourceDescription, clientName: clientName)
    }

    public static func file(title: String? = nil, clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .file, sourceDescription: sourceDescription, title: title, clientName: clientName)
    }

    public static func qrCode(title: String? = nil, clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .qrCode, sourceDescription: sourceDescription, title: title, clientName: clientName)
    }

    public static func payloadText(_ payload: String, clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .payload, payload: payload, sourceDescription: sourceDescription, clientName: clientName)
    }

    public static func configValue(_ config: PairingConfigDocument, clientName: String? = nil, sourceDescription: String? = nil) -> HostConnectionRequest {
        HostConnectionRequest(kind: .config, config: config, sourceDescription: sourceDescription, clientName: clientName)
    }
}
