import AnsightCore
import Foundation

internal struct AnsightVisualTreeBounds: Sendable, Equatable {
    let x: Double
    let y: Double
    let width: Double
    let height: Double

    var jsonValue: JSONValue {
        .object([
            "x": .number(x),
            "y": .number(y),
            "width": .number(width),
            "height": .number(height),
        ])
    }
}
