import AnsightCore
import Foundation

#if canImport(UIKit)
import UIKit
#endif

internal enum AnsightVisualTreeOverlaySupport {
    private static let defaultDurationMilliseconds = 5_000
    private static let maximumDurationMilliseconds = 10 * 60 * 1_000
    private static let maximumRectangles = 128
    private static let maximumMetadataEntries = 16
    private static let maximumMetadataKeyLength = 64
    private static let maximumMetadataStringLength = 256
    private static let defaultStrokeColor = "#FF3B30"

    static func showOverlay(arguments: [String: String]) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        return runOverlayAction {
            removeExpiredOverlays()
            let overlay = try createOverlay(arguments: arguments)
            if let existing = AnsightVisualTreeOverlayStore.shared.set(overlay) {
                removePlatformOverlay(existing)
            }
            try attachPlatformOverlay(overlay)
            scheduleExpiration(overlay)
            return overlayResult(overlay)
        }
        #else
        return platformUnsupported()
        #endif
    }

    static func getOverlay(arguments: [String: String]) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        return runOverlayAction {
            removeExpiredOverlays()
            let overlayId = try AnsightVisualTreeArgumentReader.requiredString(arguments, key: "overlayId")
            guard let overlay = AnsightVisualTreeOverlayStore.shared.get(overlayId) else {
                return .failure("The overlay '\(overlayId)' was not found.", errorCode: "visual_overlay_not_found")
            }

            return overlayResult(overlay)
        }
        #else
        return platformUnsupported()
        #endif
    }

    static func queryOverlays(arguments: [String: String]) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        return runOverlayAction {
            removeExpiredOverlays()
            let metadataKey = AnsightVisualTreeArgumentReader.string(arguments, key: "metadataKey")
            let metadataValue = AnsightVisualTreeArgumentReader.string(arguments, key: "metadataValue")
            let matching = AnsightVisualTreeOverlayStore.shared.all()
                .filter { overlayMatches($0, metadataKey: metadataKey, metadataValue: metadataValue) }
                .sorted { $0.createdAtUtc < $1.createdAtUtc }

            return .success(.object([
                "platform": .string(AnsightVisualTreeSupport.currentPlatform),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "count": .integer(Int64(matching.count)),
                "overlays": .array(matching.map { $0.jsonValue() }),
            ]))
        }
        #else
        return platformUnsupported()
        #endif
    }

    static func updateOverlay(arguments: [String: String]) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        return runOverlayAction {
            removeExpiredOverlays()
            let overlayId = try AnsightVisualTreeArgumentReader.requiredString(arguments, key: "overlayId")
            guard let existing = AnsightVisualTreeOverlayStore.shared.get(overlayId) else {
                return .failure("The overlay '\(overlayId)' was not found.", errorCode: "visual_overlay_not_found")
            }

            let updated = try createUpdatedOverlay(existing: existing, arguments: arguments)
            _ = AnsightVisualTreeOverlayStore.shared.set(updated)
            removePlatformOverlay(existing)
            try attachPlatformOverlay(updated)
            scheduleExpiration(updated)
            return overlayResult(updated)
        }
        #else
        return platformUnsupported()
        #endif
    }

    static func removeOverlay(arguments: [String: String]) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        return runOverlayAction {
            removeExpiredOverlays()
            let overlayId = try AnsightVisualTreeArgumentReader.requiredString(arguments, key: "overlayId")
            let removed = AnsightVisualTreeOverlayStore.shared.remove(overlayId)
            if let removed {
                removePlatformOverlay(removed)
            }

            return .success(.object([
                "platform": .string(AnsightVisualTreeSupport.currentPlatform),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "overlayId": .string(overlayId),
                "removed": .bool(removed != nil),
                "overlay": removed?.jsonValue() ?? .null,
            ]))
        }
        #else
        return platformUnsupported()
        #endif
    }

    static func clearOverlays(arguments: [String: String]) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        return runOverlayAction {
            removeExpiredOverlays()
            let metadataKey = AnsightVisualTreeArgumentReader.string(arguments, key: "metadataKey")
            let metadataValue = AnsightVisualTreeArgumentReader.string(arguments, key: "metadataValue")
            let matching = AnsightVisualTreeOverlayStore.shared.all()
                .filter { overlayMatches($0, metadataKey: metadataKey, metadataValue: metadataValue) }
                .sorted { $0.createdAtUtc < $1.createdAtUtc }

            for overlay in matching {
                _ = AnsightVisualTreeOverlayStore.shared.remove(overlay.id)
                removePlatformOverlay(overlay)
            }

            return .success(.object([
                "platform": .string(AnsightVisualTreeSupport.currentPlatform),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
                "count": .integer(Int64(matching.count)),
                "overlays": .array(matching.map { $0.jsonValue() }),
            ]))
        }
        #else
        return platformUnsupported()
        #endif
    }

    private static func createOverlay(arguments: [String: String]) throws -> AnsightVisualTreeOverlay {
        let overlayId = AnsightVisualTreeArgumentReader.string(arguments, key: "overlayId") ?? UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        guard !overlayId.isEmpty, overlayId.count <= 128 else {
            throw AnsightVisualTreeToolError.invalidArgument("The overlayId argument must be non-empty and at most 128 characters.")
        }

        let style = try createOverlayStyle(arguments: arguments)
        let metadata = try createOverlayMetadata(arguments: arguments)
        let rectangles = try createOverlayRectangles(arguments: arguments)
        let duration = try AnsightVisualTreeArgumentReader.integer(
            arguments,
            key: "durationMs",
            defaultValue: defaultDurationMilliseconds,
            minimum: 0,
            maximum: maximumDurationMilliseconds
        )
        let createdAt = Date()
        let expiresAt = duration > 0 ? createdAt.addingTimeInterval(Double(duration) / 1000.0) : nil
        return AnsightVisualTreeOverlay(
            id: overlayId,
            rectangles: rectangles,
            style: style,
            metadata: metadata,
            createdAtUtc: createdAt,
            expiresAtUtc: expiresAt,
            durationMilliseconds: duration
        )
    }

    private static func createUpdatedOverlay(
        existing: AnsightVisualTreeOverlay,
        arguments: [String: String]
    ) throws -> AnsightVisualTreeOverlay {
        let rectangles = hasOverlayGeometryArguments(arguments)
            ? try createOverlayRectangles(arguments: arguments)
            : existing.rectangles
        let style = try createUpdatedOverlayStyle(existing: existing.style, arguments: arguments)
        let metadata = try createUpdatedOverlayMetadata(existing: existing.metadata, arguments: arguments)

        let duration: Int
        let expiresAt: Date?
        if arguments.keys.contains("durationMs") {
            duration = try AnsightVisualTreeArgumentReader.integer(
                arguments,
                key: "durationMs",
                defaultValue: existing.durationMilliseconds,
                minimum: 0,
                maximum: maximumDurationMilliseconds
            )
            expiresAt = duration > 0 ? Date().addingTimeInterval(Double(duration) / 1000.0) : nil
        } else {
            duration = existing.durationMilliseconds
            expiresAt = existing.expiresAtUtc
        }

        return AnsightVisualTreeOverlay(
            id: existing.id,
            rectangles: rectangles,
            style: style,
            metadata: metadata,
            createdAtUtc: existing.createdAtUtc,
            expiresAtUtc: expiresAt,
            durationMilliseconds: duration
        )
    }

    private static func createOverlayStyle(arguments: [String: String]) throws -> AnsightOverlayStyle {
        let strokeColor = try parseColor(
            AnsightVisualTreeArgumentReader.string(arguments, key: "strokeColor") ?? defaultStrokeColor,
            allowNone: false
        )!
        let fillColor = try parseColor(AnsightVisualTreeArgumentReader.string(arguments, key: "fillColor"), allowNone: true)
        let strokeWidth = try AnsightVisualTreeArgumentReader.double(arguments, key: "strokeWidth", defaultValue: 2, minimum: 0, maximum: 128)
        let cornerRadius = try AnsightVisualTreeArgumentReader.double(arguments, key: "cornerRadius", defaultValue: 3, minimum: 0, maximum: 256)

        guard strokeWidth > 0 || fillColor != nil else {
            throw AnsightVisualTreeToolError.invalidArgument("The overlay would be invisible because strokeWidth is zero and fillColor is empty.")
        }

        return AnsightOverlayStyle(strokeColor: strokeColor, fillColor: fillColor, strokeWidth: strokeWidth, cornerRadius: cornerRadius)
    }

    private static func createUpdatedOverlayStyle(
        existing: AnsightOverlayStyle,
        arguments: [String: String]
    ) throws -> AnsightOverlayStyle {
        var strokeColor = existing.strokeColor
        var fillColor = existing.fillColor
        var strokeWidth = existing.strokeWidth
        var cornerRadius = existing.cornerRadius

        if let rawStrokeColor = AnsightVisualTreeArgumentReader.string(arguments, key: "strokeColor") {
            strokeColor = try parseColor(rawStrokeColor, allowNone: false)!
        }

        if arguments.keys.contains("fillColor") {
            fillColor = try parseColor(AnsightVisualTreeArgumentReader.string(arguments, key: "fillColor"), allowNone: true)
        }

        if arguments.keys.contains("strokeWidth") {
            strokeWidth = try AnsightVisualTreeArgumentReader.double(arguments, key: "strokeWidth", defaultValue: strokeWidth, minimum: 0, maximum: 128)
        }

        if arguments.keys.contains("cornerRadius") {
            cornerRadius = try AnsightVisualTreeArgumentReader.double(arguments, key: "cornerRadius", defaultValue: cornerRadius, minimum: 0, maximum: 256)
        }

        guard strokeWidth > 0 || fillColor != nil else {
            throw AnsightVisualTreeToolError.invalidArgument("The overlay would be invisible because strokeWidth is zero and fillColor is empty.")
        }

        return AnsightOverlayStyle(strokeColor: strokeColor, fillColor: fillColor, strokeWidth: strokeWidth, cornerRadius: cornerRadius)
    }

    private static func createOverlayMetadata(arguments: [String: String]) throws -> [String: JSONValue] {
        guard let rawMetadata = AnsightVisualTreeArgumentReader.string(arguments, key: "metadata") else {
            return [:]
        }

        let value = try AnsightVisualTreeArgumentReader.jsonValue(rawMetadata)
        guard case .object(let metadataObject) = value else {
            throw AnsightVisualTreeToolError.invalidArgument("The metadata argument must be a JSON object.")
        }

        guard metadataObject.count <= maximumMetadataEntries else {
            throw AnsightVisualTreeToolError.invalidArgument("The metadata object can contain at most \(maximumMetadataEntries) entries.")
        }

        var metadata: [String: JSONValue] = [:]
        for (key, value) in metadataObject {
            guard !key.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  key.count <= maximumMetadataKeyLength
            else {
                throw AnsightVisualTreeToolError.invalidArgument("Metadata keys must be non-empty and at most \(maximumMetadataKeyLength) characters.")
            }

            switch value {
            case .object, .array:
                throw AnsightVisualTreeToolError.invalidArgument("Metadata values must be scalar JSON values.")
            case .string(let stringValue):
                metadata[key] = .string(String(stringValue.prefix(maximumMetadataStringLength)))
            case .integer, .number, .bool, .null:
                metadata[key] = value
            }
        }

        return metadata
    }

    private static func createUpdatedOverlayMetadata(
        existing: [String: JSONValue],
        arguments: [String: String]
    ) throws -> [String: JSONValue] {
        let mode = metadataMode(arguments)
        switch mode {
        case "clear":
            return [:]
        case "replace":
            return try createOverlayMetadata(arguments: arguments)
        case "merge":
            guard arguments.keys.contains("metadata") else {
                return existing
            }

            let patch = try createOverlayMetadata(arguments: arguments)
            let merged = existing.merging(patch) { _, new in new }
            guard merged.count <= maximumMetadataEntries else {
                throw AnsightVisualTreeToolError.invalidArgument("The metadata object can contain at most \(maximumMetadataEntries) entries.")
            }
            return merged
        default:
            throw AnsightVisualTreeToolError.invalidArgument("The metadataMode argument must be one of: merge, replace, clear.")
        }
    }

    private static func createOverlayRectangles(arguments: [String: String]) throws -> [AnsightOverlayRectangle] {
        let nodeId = AnsightVisualTreeArgumentReader.string(arguments, key: "nodeId")
        let hasCoordinates = hasCoordinateRectangleArguments(arguments)
        if let nodeId {
            guard !hasCoordinates else {
                throw AnsightVisualTreeToolError.invalidArgument("Pass either nodeId or rectangle coordinates, not both.")
            }

            let bounds = try AnsightVisualTreeSupport.boundsForNode(nodeId: nodeId)
            return [
                try createOverlayRectangle(
                    x: bounds.x,
                    y: bounds.y,
                    width: bounds.width,
                    height: bounds.height,
                    label: AnsightVisualTreeArgumentReader.string(arguments, key: "label")
                ),
            ]
        }

        let rawRectangles = try readRawOverlayRectangles(arguments: arguments)
        let coordinateSpace = coordinateSpace(arguments)
        return try rawRectangles.map { rectangle in
            try normalize(rectangle: rectangle, coordinateSpace: coordinateSpace)
        }
    }

    private static func readRawOverlayRectangles(arguments: [String: String]) throws -> [AnsightOverlayRectangle] {
        if let rawRects = AnsightVisualTreeArgumentReader.string(arguments, key: "rects") {
            return try readOverlayRectanglesJSON(rawRects)
        }

        guard let x = try AnsightVisualTreeArgumentReader.optionalDouble(arguments, key: "x"),
              let y = try AnsightVisualTreeArgumentReader.optionalDouble(arguments, key: "y"),
              let width = try AnsightVisualTreeArgumentReader.optionalDouble(arguments, key: "width"),
              let height = try AnsightVisualTreeArgumentReader.optionalDouble(arguments, key: "height")
        else {
            throw AnsightVisualTreeToolError.invalidArgument("Pass nodeId, rects, or all of x/y/width/height.")
        }

        return [
            try createOverlayRectangle(
                x: x,
                y: y,
                width: width,
                height: height,
                label: AnsightVisualTreeArgumentReader.string(arguments, key: "label")
            ),
        ]
    }

    private static func readOverlayRectanglesJSON(_ rawRects: String) throws -> [AnsightOverlayRectangle] {
        let value = try AnsightVisualTreeArgumentReader.jsonValue(rawRects)
        let rectangleValues: [JSONValue]
        switch value {
        case .object:
            rectangleValues = [value]
        case .array(let values):
            rectangleValues = values
        default:
            throw AnsightVisualTreeToolError.invalidArgument("The rects argument must be a JSON object or array.")
        }

        guard !rectangleValues.isEmpty else {
            throw AnsightVisualTreeToolError.invalidArgument("The rects argument must contain at least one rectangle object.")
        }

        guard rectangleValues.count <= maximumRectangles else {
            throw AnsightVisualTreeToolError.invalidArgument("The rects argument can contain at most \(maximumRectangles) rectangles.")
        }

        return try rectangleValues.map(readOverlayRectangle)
    }

    private static func readOverlayRectangle(_ value: JSONValue) throws -> AnsightOverlayRectangle {
        guard case .object(let object) = value else {
            throw AnsightVisualTreeToolError.invalidArgument("Every item in the rects array must be a rectangle object.")
        }

        return try createOverlayRectangle(
            x: try rectangleDouble(object, key: "x"),
            y: try rectangleDouble(object, key: "y"),
            width: try rectangleDouble(object, key: "width"),
            height: try rectangleDouble(object, key: "height"),
            label: rectangleString(object, key: "label")
        )
    }

    private static func rectangleDouble(_ object: [String: JSONValue], key: String) throws -> Double {
        guard let value = object[key] else {
            throw AnsightVisualTreeToolError.invalidArgument("The rectangle property '\(key)' is required.")
        }

        switch value {
        case .integer(let integer):
            return Double(integer)
        case .number(let number) where number.isFinite:
            return number
        case .string(let string) where Double(string)?.isFinite == true:
            return Double(string)!
        default:
            throw AnsightVisualTreeToolError.invalidArgument("The rectangle property '\(key)' must be a number.")
        }
    }

    private static func rectangleString(_ object: [String: JSONValue], key: String) -> String? {
        guard case .string(let value)? = object[key] else {
            return nil
        }

        return value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : value
    }

    private static func createOverlayRectangle(
        x: Double,
        y: Double,
        width: Double,
        height: Double,
        label: String?
    ) throws -> AnsightOverlayRectangle {
        guard x.isFinite, y.isFinite, width.isFinite, height.isFinite else {
            throw AnsightVisualTreeToolError.invalidArgument("Overlay rectangle coordinates must be finite numbers.")
        }

        guard width > 0, height > 0 else {
            throw AnsightVisualTreeToolError.invalidArgument("Overlay rectangle width and height must be greater than zero.")
        }

        return AnsightOverlayRectangle(
            x: x,
            y: y,
            width: width,
            height: height,
            label: label?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false ? label : nil
        )
    }

    private static func normalize(
        rectangle: AnsightOverlayRectangle,
        coordinateSpace: String
    ) throws -> AnsightOverlayRectangle {
        _ = coordinateSpace
        return rectangle
    }

    private static func parseColor(_ rawColor: String?, allowNone: Bool) throws -> AnsightOverlayColor? {
        guard let rawColor = rawColor?.trimmingCharacters(in: .whitespacesAndNewlines),
              !rawColor.isEmpty
        else {
            if allowNone {
                return nil
            }
            throw AnsightVisualTreeToolError.invalidArgument("A stroke color is required.")
        }

        if allowNone && ["none", "transparent", "null"].contains(rawColor.lowercased()) {
            return nil
        }

        let value = namedColor(rawColor)
        guard value.hasPrefix("#") else {
            throw AnsightVisualTreeToolError.invalidArgument("The color '\(rawColor)' is not supported. Use #RGB, #ARGB, #RRGGBB, #AARRGGBB, or a common color name.")
        }

        let hex = String(value.dropFirst())
        switch hex.count {
        case 3:
            let r = try nibble(hex, 0) * 17
            let g = try nibble(hex, 1) * 17
            let b = try nibble(hex, 2) * 17
            return AnsightOverlayColor(a: 255, r: r, g: g, b: b)
        case 4:
            let a = try nibble(hex, 0) * 17
            let r = try nibble(hex, 1) * 17
            let g = try nibble(hex, 2) * 17
            let b = try nibble(hex, 3) * 17
            return AnsightOverlayColor(a: a, r: r, g: g, b: b)
        case 6:
            let r = try byte(hex, 0)
            let g = try byte(hex, 2)
            let b = try byte(hex, 4)
            return AnsightOverlayColor(a: 255, r: r, g: g, b: b)
        case 8:
            let a = try byte(hex, 0)
            let r = try byte(hex, 2)
            let g = try byte(hex, 4)
            let b = try byte(hex, 6)
            return AnsightOverlayColor(a: a, r: r, g: g, b: b)
        default:
            throw AnsightVisualTreeToolError.invalidArgument("The color '\(rawColor)' is not a valid hex color.")
        }
    }

    private static func namedColor(_ value: String) -> String {
        switch value.lowercased() {
        case "black": "#000000"
        case "white": "#FFFFFF"
        case "red": "#FF3B30"
        case "orange": "#FF9500"
        case "yellow": "#FFCC00"
        case "green": "#34C759"
        case "blue": "#007AFF"
        case "purple": "#AF52DE"
        case "pink": "#FF2D55"
        case "cyan": "#32ADE6"
        case "gray", "grey": "#8E8E93"
        default: value
        }
    }

    private static func nibble(_ hex: String, _ index: Int) throws -> Int {
        let character = Array(hex)[index]
        guard let value = character.hexDigitValue else {
            throw AnsightVisualTreeToolError.invalidArgument("The color '#\(hex)' is not a valid hex color.")
        }
        return value
    }

    private static func byte(_ hex: String, _ index: Int) throws -> Int {
        let characters = Array(hex)
        let high = characters[index]
        let low = characters[index + 1]
        guard let highValue = high.hexDigitValue,
              let lowValue = low.hexDigitValue
        else {
            throw AnsightVisualTreeToolError.invalidArgument("The color '#\(hex)' is not a valid hex color.")
        }

        return (highValue << 4) | lowValue
    }

    private static func hasOverlayGeometryArguments(_ arguments: [String: String]) -> Bool {
        AnsightVisualTreeArgumentReader.string(arguments, key: "nodeId") != nil || hasCoordinateRectangleArguments(arguments)
    }

    private static func hasCoordinateRectangleArguments(_ arguments: [String: String]) -> Bool {
        arguments.keys.contains("rects") ||
            arguments.keys.contains("x") ||
            arguments.keys.contains("y") ||
            arguments.keys.contains("width") ||
            arguments.keys.contains("height")
    }

    private static func coordinateSpace(_ arguments: [String: String]) -> String {
        if AnsightVisualTreeArgumentReader.string(arguments, key: "coordinateSpace")?.caseInsensitiveCompare("visualTree") == .orderedSame {
            return "visualTree"
        }

        return "window"
    }

    private static func metadataMode(_ arguments: [String: String]) -> String {
        guard let value = AnsightVisualTreeArgumentReader.string(arguments, key: "metadataMode") else {
            return "merge"
        }

        if value.caseInsensitiveCompare("replace") == .orderedSame {
            return "replace"
        }

        if value.caseInsensitiveCompare("clear") == .orderedSame {
            return "clear"
        }

        if value.caseInsensitiveCompare("merge") == .orderedSame {
            return "merge"
        }

        return value
    }

    private static func overlayMatches(
        _ overlay: AnsightVisualTreeOverlay,
        metadataKey: String?,
        metadataValue: String?
    ) -> Bool {
        guard let metadataKey else {
            return true
        }

        guard let value = overlay.metadata[metadataKey] else {
            return false
        }

        guard let metadataValue else {
            return true
        }

        return metadataComparisonValue(value).caseInsensitiveCompare(metadataValue) == .orderedSame
    }

    private static func metadataComparisonValue(_ value: JSONValue) -> String {
        switch value {
        case .string(let string):
            return string
        default:
            return (try? value.jsonString()) ?? ""
        }
    }

    private static func overlayResult(_ overlay: AnsightVisualTreeOverlay) -> AnsightToolExecutionResult {
        .success(.object([
            "platform": .string(AnsightVisualTreeSupport.currentPlatform),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
            "overlay": overlay.jsonValue(),
        ]))
    }

    @MainActor
    private static func removeExpiredOverlays() {
        let expired = AnsightVisualTreeOverlayStore.shared.removeExpired(now: Date())
        for overlay in expired {
            removePlatformOverlay(overlay)
        }
    }

    @MainActor
    private static func scheduleExpiration(_ overlay: AnsightVisualTreeOverlay) {
        overlay.timeoutWorkItem?.cancel()
        guard overlay.durationMilliseconds > 0 else {
            return
        }

        let workItem = DispatchWorkItem {
            #if canImport(UIKit)
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    guard let current = AnsightVisualTreeOverlayStore.shared.get(overlay.id),
                          current === overlay,
                          overlay.isExpired(Date())
                    else {
                        return
                    }

                    _ = AnsightVisualTreeOverlayStore.shared.remove(overlay.id)
                    removePlatformOverlay(overlay)
                }
            }
            #endif
        }
        overlay.timeoutWorkItem = workItem
        DispatchQueue.global(qos: .utility).asyncAfter(
            deadline: .now() + .milliseconds(overlay.durationMilliseconds),
            execute: workItem
        )
    }

    private static func runOverlayAction(_ action: @MainActor () throws -> AnsightToolExecutionResult) -> AnsightToolExecutionResult {
        #if canImport(UIKit)
        do {
            if Thread.isMainThread {
                return try MainActor.assumeIsolated {
                    try action()
                }
            }

            return try DispatchQueue.main.sync {
                try MainActor.assumeIsolated {
                    try action()
                }
            }
        } catch let error as AnsightVisualTreeToolError {
            return .failure(error.localizedDescription, errorCode: error.errorCode)
        } catch {
            return .failure(error.localizedDescription, errorCode: "visual_overlay_execution_failed")
        }
        #else
        return platformUnsupported()
        #endif
    }

    private static func platformUnsupported() -> AnsightToolExecutionResult {
        .failure(
            AnsightVisualTreeToolError.platformUnsupported.localizedDescription,
            errorCode: AnsightVisualTreeToolError.platformUnsupported.errorCode
        )
    }

    #if canImport(UIKit)
    @MainActor
    private static func attachPlatformOverlay(_ overlay: AnsightVisualTreeOverlay) throws {
        guard let window = AnsightVisualTreeSupport.activeWindow(),
              window.bounds.width > 0,
              window.bounds.height > 0
        else {
            throw AnsightVisualTreeToolError.unavailable("No active UIWindow is available for overlay rendering.")
        }

        let overlayView = AppleOverlayView(entry: overlay, frame: window.bounds)
        window.addSubview(overlayView)
        window.bringSubviewToFront(overlayView)
        overlay.platformHandle = overlayView
    }

    @MainActor
    private static func removePlatformOverlay(_ overlay: AnsightVisualTreeOverlay) {
        overlay.timeoutWorkItem?.cancel()
        overlay.timeoutWorkItem = nil
        if let overlayView = overlay.platformHandle as? UIView {
            overlayView.removeFromSuperview()
        }
        overlay.platformHandle = nil
    }
    #else
    private static func removePlatformOverlay(_ overlay: AnsightVisualTreeOverlay) {
        overlay.timeoutWorkItem?.cancel()
        overlay.timeoutWorkItem = nil
        overlay.platformHandle = nil
    }
    #endif
}
