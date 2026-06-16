import AnsightCore
import Foundation

public final class GetScreenshotTool: AnsightTool {
    private let runtime: AnsightRuntime

    public init(runtime: AnsightRuntime = .shared) {
        self.runtime = runtime
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightVisualTreeToolIds.getScreenshot,
            name: "Get Screenshot",
            description: "Captures a screenshot of the current app scene.",
            category: "ui",
            scope: AnsightToolScope.read.rawValue,
            keywords: "ui screenshot image capture",
            security: AnsightVisualTreeToolSecurityProfiles.getScreenshot,
            argumentsSchema: AnsightVisualTreeToolSchemas.getScreenshotArguments,
            resultSchema: AnsightVisualTreeToolSchemas.screenshotResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        switch AnsightVisualTreeSupport.captureScreenshot(arguments: arguments) {
        case .failure(let error):
            return .failure(error.localizedDescription, errorCode: error.errorCode)
        case .success(let screenshot):
            return createBinaryScreenshotResult(arguments: arguments, screenshot: screenshot)
        }
    }

    private func createBinaryScreenshotResult(
        arguments: [String: String],
        screenshot: AnsightVisualTreeScreenshot
    ) -> AnsightToolExecutionResult {
        guard let requestId = AnsightVisualTreeArgumentReader.string(arguments, key: AnsightToolExecutionArgumentNames.requestId) else {
            return .failure(
                "Screenshot capture requires a live tool request id for binary transfer.",
                errorCode: "visual_screenshot_binary_transfer_unavailable"
            )
        }

        let transferId = UUID()
        let queueResult = runtime.queueBinaryTransfer(
            requestId: requestId,
            transferId: transferId,
            data: screenshot.data,
            chunkBytes: 64 * 1024,
            description: "\(AnsightVisualTreeToolIds.getScreenshot):\(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased())"
        )
        guard queueResult.success else {
            return .failure(
                queueResult.message,
                errorCode: "visual_screenshot_binary_transfer_unavailable"
            )
        }

        let capturedAtUtc = AnsightClock.isoNow()
        let fileExtension = screenshot.format == "jpeg" ? "jpg" : "png"
        let mimeType = screenshot.format == "jpeg" ? "image/jpeg" : "image/png"
        return .success(.object([
            "platform": .string(AnsightVisualTreeSupport.currentPlatform),
            "capturedAtUtc": .string(capturedAtUtc),
            "format": .string(screenshot.format),
            "width": .integer(Int64(screenshot.width)),
            "height": .integer(Int64(screenshot.height)),
            "deliveryMode": .string("websocket_binary"),
            "wireProtocol": .string(PairingFileTransferWireProtocol.protocolName),
            "transferId": .string(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()),
            "downloadId": .string(requestId),
            "sizeBytes": .integer(Int64(screenshot.data.count)),
            "fileName": .string("screenshot-\(Self.fileTimestamp()).\(fileExtension)"),
            "mimeType": .string(mimeType),
            "artifactPath": .null,
            "artifactKind": .null,
            "status": .string("queued"),
            "receivedBytes": .null,
            "annotationApplied": .bool(screenshot.annotationApplied),
        ]))
    }

    private static func fileTimestamp() -> String {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = "yyyyMMdd-HHmmssSSS"
        return formatter.string(from: Date())
    }
}
