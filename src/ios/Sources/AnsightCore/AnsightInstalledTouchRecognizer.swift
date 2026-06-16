import Foundation

#if canImport(UIKit)
import UIKit

final class AnsightInstalledTouchRecognizer {
    let window: UIWindow
    let recognizer: UIGestureRecognizer
    let delegate: UIGestureRecognizerDelegate

    init(window: UIWindow, recognizer: UIGestureRecognizer, delegate: UIGestureRecognizerDelegate) {
        self.window = window
        self.recognizer = recognizer
        self.delegate = delegate
    }
}
#endif
