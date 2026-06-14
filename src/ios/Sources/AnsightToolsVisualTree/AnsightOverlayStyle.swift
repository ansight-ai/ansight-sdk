import AnsightKit
import Foundation

internal struct AnsightOverlayStyle: Sendable, Equatable {
    let strokeColor: AnsightOverlayColor
    let fillColor: AnsightOverlayColor?
    let strokeWidth: Double
    let cornerRadius: Double

    var jsonValue: JSONValue {
        .object([
            "strokeColor": .string(strokeColor.hexString),
            "fillColor": fillColor.map { .string($0.hexString) } ?? .null,
            "strokeWidth": .number(strokeWidth),
            "cornerRadius": .number(cornerRadius),
        ])
    }
}
