import Foundation

public struct AnsightScreenSnapshot: Sendable, Equatable {
    public let width: Int
    public let height: Int
    public let geometry: ScreenshotCaptureGeometry?
    public let data: Data

    public init(width: Int, height: Int, data: Data, geometry: ScreenshotCaptureGeometry? = nil) {
        self.width = width
        self.height = height
        self.data = data
        self.geometry = geometry
    }
}
