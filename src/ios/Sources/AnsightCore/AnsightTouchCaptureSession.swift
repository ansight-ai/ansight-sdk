import Foundation

#if canImport(UIKit)
import UIKit
#endif

final class AnsightTouchCaptureSession: @unchecked Sendable {
    static var isAvailable: Bool {
        #if canImport(UIKit)
        true
        #else
        false
        #endif
    }

    private let options: AnsightTouchCaptureOptions
    private let recordTouch: @Sendable (AnsightCapturedTouch) -> Void

    #if canImport(UIKit)
    private var installedRecognizers: [AnsightInstalledTouchRecognizer] = []
    private var windowDidBecomeKeyObserver: NSObjectProtocol?
    private var applicationDidBecomeActiveObserver: NSObjectProtocol?
    private var started = false
    #endif

    init(
        options: AnsightTouchCaptureOptions,
        recordTouch: @escaping @Sendable (AnsightCapturedTouch) -> Void
    ) {
        self.options = options
        self.recordTouch = recordTouch
    }

    func start() {
        #if canImport(UIKit)
        Task { @MainActor in
            startOnMainActor()
        }
        #endif
    }

    func stop() {
        #if canImport(UIKit)
        Task { @MainActor in
            stopOnMainActor()
        }
        #endif
    }

    #if canImport(UIKit)
    @MainActor
    private func startOnMainActor() {
        guard !started else {
            return
        }

        started = true
        windowDidBecomeKeyObserver = NotificationCenter.default.addObserver(
            forName: UIWindow.didBecomeKeyNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                self?.installCurrentWindows()
            }
        }
        applicationDidBecomeActiveObserver = NotificationCenter.default.addObserver(
            forName: UIApplication.didBecomeActiveNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                self?.installCurrentWindows()
            }
        }
        installCurrentWindows()
    }

    @MainActor
    private func stopOnMainActor() {
        guard started else {
            return
        }

        started = false
        if let windowDidBecomeKeyObserver {
            NotificationCenter.default.removeObserver(windowDidBecomeKeyObserver)
            self.windowDidBecomeKeyObserver = nil
        }
        if let applicationDidBecomeActiveObserver {
            NotificationCenter.default.removeObserver(applicationDidBecomeActiveObserver)
            self.applicationDidBecomeActiveObserver = nil
        }

        let recognizers = installedRecognizers
        installedRecognizers.removeAll()
        for installed in recognizers {
            installed.window.removeGestureRecognizer(installed.recognizer)
            installed.recognizer.delegate = nil
        }
    }

    @MainActor
    private func installCurrentWindows() {
        for scene in UIApplication.shared.connectedScenes {
            guard let windowScene = scene as? UIWindowScene else {
                continue
            }

            for window in windowScene.windows where !window.isHidden {
                install(window: window)
            }
        }
    }

    @MainActor
    private func install(window: UIWindow) {
        guard started,
              !installedRecognizers.contains(where: { $0.window === window })
        else {
            return
        }

        let delegate = AnsightSimultaneousGestureDelegate()
        let recognizer = AnsightWindowTouchCaptureRecognizer(options: options, recordTouch: recordTouch)
        recognizer.cancelsTouchesInView = false
        recognizer.delaysTouchesBegan = false
        recognizer.delaysTouchesEnded = false
        recognizer.delegate = delegate
        window.addGestureRecognizer(recognizer)
        installedRecognizers.append(
            AnsightInstalledTouchRecognizer(window: window, recognizer: recognizer, delegate: delegate)
        )
    }
    #endif
}
