import AnsightKit
import Foundation

internal struct AnsightOverlayRectangle: Sendable, Equatable {
    let x: Double
    let y: Double
    let width: Double
    let height: Double
    let label: String?

    var jsonValue: JSONValue {
        .object([
            "x": .number(x),
            "y": .number(y),
            "width": .number(width),
            "height": .number(height),
            "label": label.map(JSONValue.string) ?? .null,
        ])
    }
}
