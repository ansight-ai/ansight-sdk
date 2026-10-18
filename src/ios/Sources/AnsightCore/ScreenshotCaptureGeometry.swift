import Foundation
#if canImport(UIKit)
import UIKit
#endif

/// Coordinates refer to the upright saved image, normalized to 0...1.
public struct ScreenshotCaptureGeometry: Sendable, Codable, Equatable {
    public let coordinateSpace: String
    public let captureTarget: String
    public let safeAreaInsets: ScreenshotInsets?
    public let systemUi: [String: ScreenshotSystemUiRegion]

    public init(captureTarget: String, safeAreaInsets: ScreenshotInsets?, systemUi: [String: ScreenshotSystemUiRegion]) {
        self.coordinateSpace = "image-normalized"
        self.captureTarget = captureTarget
        self.safeAreaInsets = safeAreaInsets
        self.systemUi = systemUi
    }

    public var json: JSONValue { (try? JSONValue.fromEncodable(self)) ?? .null }

    private enum CodingKeys: String, CodingKey { case coordinateSpace, captureTarget, safeAreaInsets, systemUi }
    public func encode(to encoder: Encoder) throws {
        var values = encoder.container(keyedBy: CodingKeys.self)
        try values.encode(coordinateSpace, forKey: .coordinateSpace)
        try values.encode(captureTarget, forKey: .captureTarget)
        try values.encode(safeAreaInsets, forKey: .safeAreaInsets)
        try values.encode(systemUi, forKey: .systemUi)
    }

    #if canImport(UIKit)
    @MainActor
    static func appWindow(_ window: UIWindow) -> ScreenshotCaptureGeometry {
        let size = window.bounds.size
        let insets = window.safeAreaInsets
        let safe = size.width > 0 && size.height > 0
            ? ScreenshotInsets(top: Double(insets.top / size.height), right: Double(insets.right / size.width),
                bottom: Double(insets.bottom / size.height), left: Double(insets.left / size.width)) : nil
        return ScreenshotCaptureGeometry(captureTarget: "appWindow", safeAreaInsets: safe?.isValid == true ? safe : nil,
            systemUi: [
                "statusBar": .absent("App-window rendering excludes the OS status-bar layer."),
                "navigationBar": .absent("App-window rendering excludes the OS home-indicator layer."),
                "displayCutout": .unknown("Public APIs do not provide exact cutout bounds for this image."),
            ])
    }
    #endif
}

public struct ScreenshotInsets: Sendable, Codable, Equatable {
    public let top: Double
    public let right: Double
    public let bottom: Double
    public let left: Double
    public init(top: Double, right: Double, bottom: Double, left: Double) {
        self.top = top; self.right = right; self.bottom = bottom; self.left = left
    }
    public var isValid: Bool {
        [top, right, bottom, left].allSatisfy { $0.isFinite && $0 >= 0 && $0 <= 1 }
            && top + bottom < 1 && left + right < 1
    }
}

public struct ScreenshotRect: Sendable, Codable, Equatable {
    public let x: Double
    public let y: Double
    public let width: Double
    public let height: Double
    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x; self.y = y; self.width = width; self.height = height
    }
}

public struct ScreenshotSystemUiRegion: Sendable, Codable, Equatable {
    public let state: String
    public let regions: [ScreenshotRect]
    public let reason: String?
    public init(state: String, regions: [ScreenshotRect] = [], reason: String? = nil) {
        self.state = state; self.regions = regions; self.reason = reason
    }
    public static func absent(_ reason: String) -> Self { Self(state: "absent", reason: reason) }
    public static func unknown(_ reason: String) -> Self { Self(state: "unknown", reason: reason) }
}
