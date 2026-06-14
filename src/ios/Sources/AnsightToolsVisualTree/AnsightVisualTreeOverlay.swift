import AnsightKit
import Foundation

internal final class AnsightVisualTreeOverlay: @unchecked Sendable {
    let id: String
    let rectangles: [AnsightOverlayRectangle]
    let style: AnsightOverlayStyle
    let metadata: [String: JSONValue]
    let createdAtUtc: Date
    let expiresAtUtc: Date?
    let durationMilliseconds: Int

    var timeoutWorkItem: DispatchWorkItem?
    var platformHandle: AnyObject?

    init(
        id: String,
        rectangles: [AnsightOverlayRectangle],
        style: AnsightOverlayStyle,
        metadata: [String: JSONValue],
        createdAtUtc: Date,
        expiresAtUtc: Date?,
        durationMilliseconds: Int
    ) {
        self.id = id
        self.rectangles = rectangles
        self.style = style
        self.metadata = metadata
        self.createdAtUtc = createdAtUtc
        self.expiresAtUtc = expiresAtUtc
        self.durationMilliseconds = durationMilliseconds
    }

    func isExpired(_ now: Date) -> Bool {
        guard let expiresAtUtc else {
            return false
        }

        return now >= expiresAtUtc
    }

    func jsonValue(now: Date = Date()) -> JSONValue {
        .object([
            "id": .string(id),
            "platform": .string(AnsightVisualTreeSupport.currentPlatform),
            "createdAtUtc": .string(AnsightVisualTreeOverlay.isoString(createdAtUtc)),
            "expiresAtUtc": expiresAtUtc.map { .string(AnsightVisualTreeOverlay.isoString($0)) } ?? .null,
            "durationMs": .integer(Int64(durationMilliseconds)),
            "remainingMs": expiresAtUtc.map { .integer(Int64(max(0, Int($0.timeIntervalSince(now) * 1000)))) } ?? .null,
            "transient": .bool(durationMilliseconds > 0),
            "inputTransparent": .bool(true),
            "coordinateSpace": .string("window"),
            "style": style.jsonValue,
            "rects": .array(rectangles.map(\.jsonValue)),
            "metadata": .object(metadata),
        ])
    }

    private static func isoString(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
