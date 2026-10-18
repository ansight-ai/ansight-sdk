import Foundation
import XCTest
@testable import AnsightCore

final class ArtifactPublicationTests: XCTestCase {
    private var metadata: JSONValue { .object([
        "providerId": .string("scene"), "artifactId": .string("model"), "name": .string("Model"),
        "kind": .string("model"), "mimeType": .string("model/gltf-binary"), "fileName": .string("model.glb")
    ]) }

    func testChunksAndCommitAcknowledgement() async {
        let recorder = Recorder()
        let bytes = Data((0..<65537).map { UInt8($0 % 251) })
        let result = await AnsightArtifactPublication.send(metadata: metadata, data: bytes,
            control: { action, _ in await recorder.control(action) }, binary: { await recorder.binary($0) })
        XCTAssertTrue(result.success)
        let actions = await recorder.actions
        let frames = await recorder.frames
        XCTAssertEqual(actions, ["artifact.publish.begin", "artifact.publish.commit"])
        XCTAssertEqual(frames.count, 3)
        XCTAssertEqual(Data(frames[0].dropFirst(56)) + Data(frames[1].dropFirst(56)), bytes)
        XCTAssertEqual(frames.last?[5], 2)
    }

    func testCommitRejectionIsFailureAndAborts() async {
        let recorder = Recorder(rejectCommit: true)
        let result = await AnsightArtifactPublication.send(metadata: metadata, data: Data(),
            control: { action, _ in await recorder.control(action) }, binary: { await recorder.binary($0) })
        XCTAssertFalse(result.success)
        let actions = await recorder.actions
        XCTAssertEqual(actions.last, "artifact.publish.abort")
    }

    func testInvalidMetadataDoesNotStartTransfer() async {
        let result = await AnsightArtifactPublication.send(metadata: .object([:]), data: Data(),
            control: { _, _ in XCTFail("No upload expected"); return .success("bad") },
            binary: { _ in XCTFail("No bytes expected"); return .success("bad") })
        XCTAssertFalse(result.success)
    }

    func testCancellationAfterCompleteDoesNotCommit() async {
        let recorder = Recorder()
        let metadata = metadata
        let task = Task {
            await AnsightArtifactPublication.send(metadata: metadata, data: Data(),
                control: { action, _ in await recorder.control(action) },
                binary: { data in
                    withUnsafeCurrentTask { $0?.cancel() }
                    return await recorder.binary(data)
                })
        }
        let result = await task.value
        XCTAssertFalse(result.success)
        let actions = await recorder.actions
        XCTAssertEqual(actions, ["artifact.publish.begin", "artifact.publish.abort"])
    }

    func testFunctionJSONToolPreservesStructuredArgumentsAndAvailability() throws {
        let tool = AnsightFunctionJSONTool(descriptor: AnsightToolDescriptor(id: "scene.echo", name: "Echo", policy: .read),
            availability: { _ in .unavailable(reasonCode: "scene_loading", reason: "Scene loading") },
            handler: { args in .success(.object(args)) })
        let result = try tool.execute(arguments: ["visible": JSONValue.bool(true)])
        XCTAssertEqual(result.result, .object(["visible": .bool(true)]))
        XCTAssertEqual(tool.availability(context: .init(sessionId: nil, requestId: nil)).reasonCode, "scene_loading")
    }

    private actor Recorder {
        var actions: [String] = []
        var frames: [Data] = []
        let rejectCommit: Bool
        init(rejectCommit: Bool = false) { self.rejectCommit = rejectCommit }
        func control(_ action: String) -> OperationResult {
            actions.append(action)
            return rejectCommit && action == "artifact.publish.commit" ? .failure("disk full") : .success("retained")
        }
        func binary(_ data: Data) -> OperationResult { frames.append(data); return .success("sent") }
    }
}
