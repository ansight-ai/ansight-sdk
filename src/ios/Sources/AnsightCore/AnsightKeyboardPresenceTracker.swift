import Foundation

#if canImport(UIKit)
import UIKit

@MainActor
final class AnsightKeyboardPresenceTracker: NSObject {
    static let shared = AnsightKeyboardPresenceTracker()

    private var observing = false
    private var lastKnownPresence: Bool?

    func currentPresence() -> Bool {
        startObservingIfNeeded()

        if let window = activeWindow(), keyboardLayoutGuideIndicatesPresence(in: window) {
            lastKnownPresence = true
            return true
        }

        return lastKnownPresence ?? false
    }

    private func startObservingIfNeeded() {
        guard !observing else {
            return
        }

        observing = true
        let center = NotificationCenter.default
        center.addObserver(
            self,
            selector: #selector(keyboardFrameChanged(_:)),
            name: UIResponder.keyboardWillChangeFrameNotification,
            object: nil
        )
        center.addObserver(
            self,
            selector: #selector(keyboardShown(_:)),
            name: UIResponder.keyboardDidShowNotification,
            object: nil
        )
        center.addObserver(
            self,
            selector: #selector(keyboardHidden(_:)),
            name: UIResponder.keyboardDidHideNotification,
            object: nil
        )
    }

    @objc
    private func keyboardFrameChanged(_ notification: Notification) {
        guard let frame = notification.userInfo?[UIResponder.keyboardFrameEndUserInfoKey] as? CGRect,
              let window = activeWindow()
        else {
            return
        }

        let windowFrame = window.convert(frame, from: window.screen.coordinateSpace)
        let intersection = window.bounds.intersection(windowFrame)
        lastKnownPresence = !intersection.isNull && intersection.height > 1
    }

    @objc
    private func keyboardShown(_ notification: Notification) {
        keyboardFrameChanged(notification)
        if lastKnownPresence == nil {
            lastKnownPresence = true
        }
    }

    @objc
    private func keyboardHidden(_ notification: Notification) {
        lastKnownPresence = false
    }

    private func keyboardLayoutGuideIndicatesPresence(in window: UIWindow) -> Bool {
        window.layoutIfNeeded()
        let intersection = window.bounds.intersection(window.keyboardLayoutGuide.layoutFrame)
        return !intersection.isNull && intersection.height > 1
    }

    private func activeWindow() -> UIWindow? {
        let scenes = UIApplication.shared.connectedScenes
            .compactMap { $0 as? UIWindowScene }
            .filter { $0.activationState == .foregroundActive || $0.activationState == .foregroundInactive }

        return scenes.flatMap(\.windows).first { $0.isKeyWindow }
            ?? scenes.flatMap(\.windows).first { !$0.isHidden && $0.alpha > 0 }
            ?? UIApplication.shared.windows.first { $0.isKeyWindow }
    }
}
#endif
