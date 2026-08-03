#if canImport(UIKit)
import UIKit
import XCTest
@testable import AnsightCore

final class AnsightLifecycleObserverTests: XCTestCase {
    func testSceneLifecycleResolutionReportsBackgroundWhenAllScenesAreBackgrounded() {
        let state = AnsightLifecycleObserver.resolveLifecycleState(
            sceneStates: [.background, .unattached],
            applicationState: .background
        )

        XCTAssertEqual(state, .background)
    }

    func testSceneLifecycleResolutionKeepsAppForegroundWhileAnySceneIsForegrounded() {
        let state = AnsightLifecycleObserver.resolveLifecycleState(
            sceneStates: [.background, .foregroundInactive],
            applicationState: .inactive,
            fallbackState: .background
        )

        XCTAssertEqual(state, .foreground)
    }

    func testSceneWillEnterForegroundWinsOverBackgroundApplicationState() {
        let state = AnsightLifecycleObserver.resolveLifecycleState(
            sceneStates: [.foregroundInactive],
            applicationState: .background,
            fallbackState: .foreground
        )

        XCTAssertEqual(state, .foreground)
    }

    func testOneSceneEnteringBackgroundDoesNotHideAnotherForegroundScene() {
        let state = AnsightLifecycleObserver.resolveLifecycleState(
            sceneStates: [.foregroundActive, .background],
            applicationState: .inactive,
            fallbackState: .background
        )

        XCTAssertEqual(state, .foreground)
    }
}
#endif
