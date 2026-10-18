import Foundation
import AnsightCore

internal struct AnsightVisualTreeScreenshot: Sendable {
    let format: String
    let width: Int
    let height: Int
    let data: Data
    let annotationApplied: Bool
    var geometry: ScreenshotCaptureGeometry? = nil
}
