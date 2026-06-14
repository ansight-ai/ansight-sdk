import Foundation

enum AnsightTouchInputWireProtocol {
    static let maxBatchSize = 200
    static let maxPendingTouches = 2_000
    static let touchInputType = "CLIENT_TOUCH_INPUT"
    static let schema = "ansight.touches.v1"

    static func payloads(for touches: [AnsightCapturedTouch]) -> [JSONValue] {
        let sortedTouches = touches.sorted {
            if $0.capturedAt == $1.capturedAt {
                return $0.id < $1.id
            }
            return $0.capturedAt < $1.capturedAt
        }

        var groups: [AnsightTouchBatchKey: [AnsightCapturedTouch]] = [:]
        for touch in sortedTouches {
            groups[batchKey(for: touch), default: []].append(touch)
        }

        return groups.values
            .sorted { left, right in
                guard let leftTouch = left.first, let rightTouch = right.first else {
                    return left.count < right.count
                }
                return leftTouch.capturedAt < rightTouch.capturedAt
            }
            .map(makePayload)
    }

    private static func makePayload(for touches: [AnsightCapturedTouch]) -> JSONValue {
        guard let firstTouch = touches.first else {
            return .object([:])
        }

        let key = batchKey(for: firstTouch)
        return .object([
            "type": .string(touchInputType),
            "schema": .string(schema),
            "t0": .string(AnsightClock.isoString(from: firstTouch.capturedAt)),
            "space": .string(key.space),
            "unit": .string(key.unit),
            "surface": .array([
                jsonNumberOrNull(key.surfaceWidth),
                jsonNumberOrNull(key.surfaceHeight),
                jsonNumberOrNull(key.surfaceScale),
            ]),
            "rows": .array(touches.map { touch in
                .array(row(for: touch, t0: firstTouch.capturedAt))
            }),
        ])
    }

    private static func row(for touch: AnsightCapturedTouch, t0: Date) -> [JSONValue] {
        let deltaMilliseconds = max(
            0,
            Int64((touch.capturedAt.timeIntervalSince(t0) * 1_000).rounded(.toNearestOrAwayFromZero))
        )
        var row: [JSONValue] = [
            .integer(deltaMilliseconds),
            .integer(Int64(touch.action.wireCode)),
            .integer(touch.pointerId),
            .number(touch.x),
            .number(touch.y),
        ]

        if touch.pointerIndex != 0 || touch.pointerCount != 1 {
            row.append(.integer(Int64(touch.pointerIndex)))
            row.append(.integer(Int64(max(touch.pointerIndex + 1, touch.pointerCount))))
        }

        return row
    }

    private static func batchKey(for touch: AnsightCapturedTouch) -> AnsightTouchBatchKey {
        AnsightTouchBatchKey(
            space: encodeSpace(touch.coordinateSpace),
            unit: encodeUnit(touch.coordinateUnit),
            surfaceWidth: normalizedPositiveValue(touch.surfaceWidth),
            surfaceHeight: normalizedPositiveValue(touch.surfaceHeight),
            surfaceScale: normalizedPositiveValue(touch.surfaceScale)
        )
    }

    private static func encodeSpace(_ coordinateSpace: String) -> String {
        let normalized = coordinateSpace.trimmingCharacters(in: .whitespacesAndNewlines)
        return normalized.isEmpty || normalized.caseInsensitiveCompare("window") == .orderedSame
            ? "w"
            : normalized
    }

    private static func encodeUnit(_ coordinateUnit: String) -> String {
        switch coordinateUnit.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "pixels", "pixel", "px":
            return "px"
        case "points", "point", "pt":
            return "pt"
        case "normalized", "unit", "ratio", "n":
            return "n"
        case let value where value.isEmpty:
            return "px"
        case let value:
            return value
        }
    }

    private static func normalizedPositiveValue(_ value: Double?) -> Double? {
        guard let value, value.isFinite, value > 0 else {
            return nil
        }
        return value
    }

    private static func jsonNumberOrNull(_ value: Double?) -> JSONValue {
        guard let value else {
            return .null
        }
        return .number(value)
    }
}
