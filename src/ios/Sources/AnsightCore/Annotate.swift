import Foundation

#if canImport(UIKit)
import UIKit
#endif

/// Captures app evidence, presents the native annotation editor, and submits a session bundle.
public enum Annotate {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var options = AnnotationOptions()
    nonisolated(unsafe) private static var initialized = false
    nonisolated(unsafe) private static var presenting = false
    nonisolated(unsafe) private static var flushing = false
    nonisolated(unsafe) private static var connectionSubscription: HostConnectionStatusSubscription?

    internal static func configure(_ options: AnnotationOptions) {
        connectionSubscription?.remove()
        connectionSubscription = nil
        lock.withLock {
            self.options = options.validated()
            initialized = true
        }
    }

    internal static func attachToRuntime() {
        connectionSubscription = AnsightRuntime.shared.addHostConnectionStatusListener { status, _ in
            #if canImport(UIKit)
            if status.isConnected { Task { await flushOutbox() } }
            #endif
        }
    }

    public static var isEnabled: Bool {
        lock.withLock {
            #if DEBUG
            let compiledDebugBuild = true
            #else
            let compiledDebugBuild = false
            #endif
            return initialized && options.enabled && (options.debugBuildOverride ?? compiledDebugBuild)
        }
    }

    /// Captures the current screen and registered visual trees before showing the editor.
    public static func PresentAsync() async -> AnnotationCaptureResult {
        guard isEnabled else {
            return AnnotationCaptureResult(status: .disabled, message: "Annotations require an initialized Debug application.")
        }
        let mayPresent = lock.withLock { () -> Bool in
            if presenting { return false }
            presenting = true
            return true
        }
        guard mayPresent else {
            return AnnotationCaptureResult(status: .unavailable, message: "An annotation editor is already open.")
        }
        defer { lock.withLock { presenting = false } }

        #if canImport(UIKit)
        let captureOptions = lock.withLock { options }
        let annotationId = UUID()
        let captureGroupId = UUID()
        let capturedAtUtc = AnsightClock.isoNow()
        let screenshot = captureOptions.captureScreenshot
            ? try? await AnsightScreenSnapshotRenderer.captureIncludingGpuBackedSurfaces(
                quality: captureOptions.screenshotQuality,
                maxWidth: captureOptions.screenshotMaxWidth
            )
            : nil
        let screenshotAtUtc = AnsightClock.isoNow()
        let visualTrees = captureOptions.captureVisualTrees
            ? AnsightSessionVisualTreeCaptureRegistry.capture()
            : []
        let treeAtUtc = AnsightClock.isoNow()
        guard let draft = await AnnotationEditorViewController.present(screenshot: screenshot) else {
            return AnnotationCaptureResult(status: .cancelled)
        }
        do {
            let bundle = try makeBundle(
                annotationId: annotationId,
                captureGroupId: captureGroupId,
                capturedAtUtc: capturedAtUtc,
                screenshotAtUtc: screenshotAtUtc,
                screenshot: screenshot,
                treeAtUtc: treeAtUtc,
                visualTrees: visualTrees,
                captureOptions: captureOptions,
                draft: draft
            )
            let outboxURL = try store(bundle, annotationId: annotationId, capturedAtUtc: capturedAtUtc)
            let delivery = await submit(bundle, annotationId: annotationId, capturedAtUtc: capturedAtUtc)
            if delivery.success {
                try? FileManager.default.removeItem(at: outboxURL)
                try? FileManager.default.removeItem(at: outboxURL.deletingPathExtension().appendingPathExtension("json"))
            }
            return AnnotationCaptureResult(
                status: delivery.success ? .completed : .queued,
                annotationId: annotationId,
                message: delivery.message
            )
        } catch {
            return AnnotationCaptureResult(status: .failed, annotationId: annotationId, message: error.localizedDescription)
        }
        #else
        return AnnotationCaptureResult(status: .unavailable, message: "The native editor requires UIKit.")
        #endif
    }

    #if canImport(UIKit)
    private static func makeBundle(
        annotationId: UUID,
        captureGroupId: UUID,
        capturedAtUtc: String,
        screenshotAtUtc: String,
        screenshot: AnsightScreenSnapshot?,
        treeAtUtc: String,
        visualTrees: [JSONValue],
        captureOptions: AnnotationOptions,
        draft: AnnotationDraft
    ) throws -> Data {
        var entries: [(String, Data)] = []
        var manifest: [String: Any] = [
            "schema": "ansight.annotation.bundle.v1",
            "version": 1,
            "annotationId": annotationId.uuidString.lowercased(),
            "captureGroupId": captureGroupId.uuidString.lowercased(),
            "capturedAtUtc": capturedAtUtc,
            "feedback": draft.feedback,
            "shapes": draft.paths.map { path -> [String: Any] in
                let x = path.map(\.x).min() ?? 0
                let y = path.map(\.y).min() ?? 0
                return [
                    "kind": "freeDraw",
                    "x": x, "y": y,
                    "width": (path.map(\.x).max() ?? x) - x,
                    "height": (path.map(\.y).max() ?? y) - y,
                    "points": path.map { ["x": $0.x, "y": $0.y] },
                    "text": "", "strokeColor": "#FFFF3B30", "strokeWidth": 3
                ]
            },
            "visualTrees": [], "artifacts": [], "evidence": []
        ]
        if let screenshot {
            let path = "evidence/screenshot.jpg"
            entries.append((path, screenshot.data))
            manifest["screenshot"] = [
                "path": path, "mimeType": "image/jpeg",
                "width": screenshot.width, "height": screenshot.height,
                "capturedAtUtc": screenshotAtUtc
            ]
        }
        var evidence: [[String: Any]] = [[
            "id": "screenshot", "kind": "screenshot",
            "status": screenshot == nil
                ? (captureOptions.captureScreenshot ? "unavailable" : "skipped")
                : "captured",
            "capturedAtUtc": screenshot == nil ? NSNull() : screenshotAtUtc as Any,
            "sizeBytes": screenshot.map { $0.data.count as Any } ?? NSNull()
        ]]
        var treeManifests: [[String: Any]] = []
        for (index, tree) in visualTrees.enumerated() {
            let path = "evidence/visual-trees/tree-\(index).json"
            entries.append((path, try tree.jsonData()))
            let source = tree.annotationObject?["source"]?.stringValue ?? "native"
            treeManifests.append([
                "source": source,
                "displayName": source,
                "path": path,
                "capturedAtUtc": treeAtUtc,
                "truncated": tree.annotationObject?["truncated"] == .bool(true)
            ])
            evidence.append([
                "id": "visual-tree:\(source)", "kind": "visualTree", "status": "captured",
                "capturedAtUtc": treeAtUtc, "sizeBytes": try tree.jsonData().count
            ])
        }
        if visualTrees.isEmpty {
            evidence.append([
                "id": "visual-tree", "kind": "visualTree",
                "status": captureOptions.captureVisualTrees ? "unavailable" : "skipped"
            ])
        }
        manifest["visualTrees"] = treeManifests
        manifest["evidence"] = evidence
        entries.append(("manifest.json", try JSONSerialization.data(withJSONObject: manifest)))
        return try AnnotationZip.make(entries)
    }

    private static func store(_ bundle: Data, annotationId: UUID, capturedAtUtc: String) throws -> URL {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Ansight/Annotations/Outbox", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let url = root.appendingPathComponent("\(annotationId.uuidString).ansightannotation")
        try bundle.write(to: url, options: .atomic)
        try Data(capturedAtUtc.utf8).write(
            to: url.deletingPathExtension().appendingPathExtension("json"), options: .atomic
        )
        return url
    }

    private static func flushOutbox() async {
        let mayFlush = lock.withLock { () -> Bool in
            if flushing { return false }
            flushing = true
            return true
        }
        guard mayFlush else { return }
        defer { lock.withLock { flushing = false } }
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Ansight/Annotations/Outbox", isDirectory: true)
        let files = (try? FileManager.default.contentsOfDirectory(
            at: root, includingPropertiesForKeys: [.contentModificationDateKey]
        )) ?? []
        for url in files.filter({ $0.pathExtension == "ansightannotation" }).sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {
            let metadataURL = url.deletingPathExtension().appendingPathExtension("json")
            guard let annotationId = UUID(uuidString: url.deletingPathExtension().lastPathComponent),
                  let capturedAtUtc = try? String(contentsOf: metadataURL, encoding: .utf8),
                  let bundle = try? Data(contentsOf: url) else { continue }
            let result = await submit(bundle, annotationId: annotationId, capturedAtUtc: capturedAtUtc)
            guard result.success else { break }
            try? FileManager.default.removeItem(at: url)
            try? FileManager.default.removeItem(at: metadataURL)
        }
    }

    private static func submit(_ bundle: Data, annotationId: UUID, capturedAtUtc: String) async -> OperationResult {
        let transferId = UUID()
        let chunkBytes = 64 * 1024
        let payload: JSONValue = .object([
            "schema": .string("ansight.annotation.submit.v1"),
            "clientAnnotationId": .string(annotationId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()),
            "capturedAtUtc": .string(capturedAtUtc),
            "transfer": .object([
                "transferId": .string(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()),
                "fileName": .string("\(annotationId.uuidString).ansightannotation"),
                "mimeType": .string("application/vnd.ansight.annotation+zip"),
                "sizeBytes": .integer(Int64(bundle.count)),
                "chunkBytes": .integer(Int64(chunkBytes)),
                "wireProtocol": .string(PairingFileTransferWireProtocol.protocolName)
            ])
        ])
        let ready = await AnsightRuntime.shared.sendControlRequest(action: "annotation.submit", payload: payload)
        guard ready.success else { return ready }
        var offset = 0
        var sequence: Int32 = 0
        while offset < bundle.count {
            let end = min(offset + chunkBytes, bundle.count)
            let frame = PairingFileTransferWireProtocol.createFrame(
                transferId: transferId, frameType: .chunk, sequence: sequence,
                offsetBytes: Int64(offset), payload: bundle.subdata(in: offset..<end)
            )
            let result = await AnsightRuntime.shared.sendBinaryData(frame)
            guard result.success else { return result }
            offset = end
            sequence += 1
        }
        return await AnsightRuntime.shared.sendBinaryData(PairingFileTransferWireProtocol.createFrame(
            transferId: transferId, frameType: .complete, sequence: sequence,
            offsetBytes: Int64(bundle.count), payload: Data()
        ))
    }
    #endif
}

private extension JSONValue {
    var annotationObject: [String: JSONValue]? {
        if case .object(let value) = self { return value }
        return nil
    }
}
