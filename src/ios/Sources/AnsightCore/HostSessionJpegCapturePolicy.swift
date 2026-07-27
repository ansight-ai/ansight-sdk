import Foundation

struct HostSessionJpegCapturePolicy: Sendable, Equatable {
    static let controlVersionPropertyName = "sessionJpegCaptureControlVersion"
    static let controlVersion: Int64 = 1

    let useHostCapture: Bool
    let source: String?

    static let app = HostSessionJpegCapturePolicy(useHostCapture: false, source: nil)

    init(payload: JSONValue?) {
        guard case .object(let root) = payload,
              case .object(let capture)? = root["sessionJpegCapture"],
              capture["mode"]?.stringValue?.lowercased() == "host"
        else {
            self = .app
            return
        }

        useHostCapture = true
        source = capture["source"]?.stringValue
    }

    private init(useHostCapture: Bool, source: String?) {
        self.useHostCapture = useHostCapture
        self.source = source
    }
}
