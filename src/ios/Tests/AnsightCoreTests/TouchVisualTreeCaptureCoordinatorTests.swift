@testable import AnsightCore
import XCTest

final class TouchVisualTreeCaptureCoordinatorTests: XCTestCase {
    func testGestureCapturesOnlyDownAndUpTrees() async throws {
        let recorder = TouchVisualTreeTriggerRecorder()
        let coordinator = AnsightTouchVisualTreeCaptureCoordinator { trigger in
            await recorder.append(trigger)
        }

        coordinator.observe(makeTouch(action: .down, pointerId: 7))
        try await waitForTriggerCount(1, recorder: recorder)
        coordinator.observe(makeTouch(action: .move, pointerId: 7))
        try await Task.sleep(nanoseconds: 350_000_000)
        let triggerCountAfterMove = await recorder.values.count
        XCTAssertEqual(triggerCountAfterMove, 1)
        coordinator.observe(makeTouch(action: .up, pointerId: 7))
        try await waitForTriggerCount(2, recorder: recorder)
        coordinator.close()

        let triggers = await recorder.values
        XCTAssertEqual(triggers.count, 2)
        XCTAssertEqual(triggers.first?.gesturePhase, .started)
        XCTAssertEqual(triggers.first?.touchAction, "down")
        XCTAssertEqual(triggers.last?.gesturePhase, .ended)
        XCTAssertEqual(triggers.last?.touchAction, "up")
        XCTAssertEqual(Set(triggers.map(\.gestureId)).count, 1)
    }

    func testCancelDoesNotCaptureTree() async throws {
        let recorder = TouchVisualTreeTriggerRecorder()
        let coordinator = AnsightTouchVisualTreeCaptureCoordinator { trigger in
            await recorder.append(trigger)
        }

        coordinator.observe(makeTouch(action: .down, pointerId: 7))
        try await waitForTriggerCount(1, recorder: recorder)
        coordinator.observe(makeTouch(action: .cancel, pointerId: 7))
        try await Task.sleep(nanoseconds: 100_000_000)
        coordinator.close()

        let triggers = await recorder.values
        XCTAssertEqual(triggers.count, 1)
        XCTAssertEqual(triggers.first?.gesturePhase, .started)
        XCTAssertEqual(triggers.first?.touchAction, "down")
    }

    private func waitForTriggerCount(
        _ count: Int,
        recorder: TouchVisualTreeTriggerRecorder
    ) async throws {
        for _ in 0..<100 {
            if await recorder.values.count >= count {
                return
            }
            try await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTFail("Timed out waiting for \(count) touch visual-tree triggers.")
    }

    private func makeTouch(
        action: AnsightCapturedTouchAction,
        pointerId: Int64
    ) -> AnsightCapturedTouch {
        AnsightCapturedTouch(
            action: action,
            pointerId: pointerId,
            pointerIndex: 0,
            pointerCount: 1,
            x: 24,
            y: 48,
            surfaceWidth: 200,
            surfaceHeight: 400,
            coordinateUnit: "points",
            surfaceScale: 2
        )
    }
}

private actor TouchVisualTreeTriggerRecorder {
    private(set) var values: [AnsightTouchVisualTreeCaptureTrigger] = []

    func append(_ trigger: AnsightTouchVisualTreeCaptureTrigger) {
        values.append(trigger)
    }
}
