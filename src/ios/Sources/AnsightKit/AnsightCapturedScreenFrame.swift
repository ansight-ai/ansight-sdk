import Foundation

public struct AnsightCapturedScreenFrame: Sendable, Equatable {
    public let capturedAtUtc: String
    public let capturedAtEpochMilliseconds: Int64
    public let width: Int
    public let height: Int
    public let quality: Int
    public let jpegData: Data

    public init(
        capturedAtUtc: String,
        capturedAtEpochMilliseconds: Int64,
        width: Int,
        height: Int,
        quality: Int,
        jpegData: Data
    ) {
        self.capturedAtUtc = capturedAtUtc
        self.capturedAtEpochMilliseconds = capturedAtEpochMilliseconds
        self.width = width
        self.height = height
        self.quality = quality
        self.jpegData = jpegData
    }
}
