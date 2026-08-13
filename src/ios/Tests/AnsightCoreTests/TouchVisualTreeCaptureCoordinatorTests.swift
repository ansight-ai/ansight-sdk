@testable import AnsightCore
import XCTest

final class TouchVisualTreeCaptureCoordinatorTests: XCTestCase {
    func testGestureCapturesLeadingCheckpointAndTerminalTrees() async throws {
        let recorder = TouchVisualTreeTriggerRecorder()
        let coordinator = AnsightTouchVisualTreeCaptureCoordinator(
            checkpointIntervalNanoseconds: 20_000_000
        ) { trigger in
            await recorder.append(trigger)
        }

        coordinator.observe(makeTouch(action: .down, pointerId: 7))
        try await waitForTriggerCount(2, recorder: recorder)
        coordinator.observe(makeTouch(action: .up, pointerId: 7))
        try await waitForTerminalTrigger(recorder: recorder)
        coordinator.close()

        let triggers = await recorder.values
        XCTAssertEqual(triggers.first?.gesturePhase, .started)
        XCTAssertTrue(triggers.contains { $0.gesturePhase == .checkpoint })
        XCTAssertEqual(triggers.last?.gesturePhase, .ended)
        XCTAssertEqual(Set(triggers.map(\.gestureId)).count, 1)
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

    private func waitForTerminalTrigger(
        recorder: TouchVisualTreeTriggerRecorder
    ) async throws {
        for _ in 0..<100 {
            if await recorder.values.last?.gesturePhase == .ended {
                return
            }
            try await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTFail("Timed out waiting for the terminal touch visual-tree trigger.")
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
