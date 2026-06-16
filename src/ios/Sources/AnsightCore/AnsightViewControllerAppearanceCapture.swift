import Foundation

#if canImport(UIKit)
import ObjectiveC.runtime
import UIKit

final class AnsightViewControllerAppearanceCapture: @unchecked Sendable {
    static let shared = AnsightViewControllerAppearanceCapture()

    private let lock = NSLock()
    private weak var observer: AnsightLifecycleObserver?
    private var installed = false

    private init() {}

    func start(observer: AnsightLifecycleObserver) {
        installIfNeeded()
        lock.withLock {
            self.observer = observer
        }
    }

    func stop(observer: AnsightLifecycleObserver) {
        lock.withLock {
            if self.observer === observer {
                self.observer = nil
            }
        }
    }

    func viewControllerDidAppear(_ viewController: UIViewController) {
        let observer = lock.withLock { self.observer }
        observer?.viewControllerDidAppear(viewController)
    }

    private func installIfNeeded() {
        lock.lock()
        if installed {
            lock.unlock()
            return
        }
        installed = true
        lock.unlock()

        guard let originalMethod = class_getInstanceMethod(
            UIViewController.self,
            #selector(UIViewController.viewDidAppear(_:))
        ),
            let replacementMethod = class_getInstanceMethod(
                UIViewController.self,
                #selector(UIViewController.ansight_viewDidAppear(_:))
            )
        else {
            return
        }

        method_exchangeImplementations(originalMethod, replacementMethod)
    }
}

private extension UIViewController {
    @objc
    func ansight_viewDidAppear(_ animated: Bool) {
        ansight_viewDidAppear(animated)
        AnsightViewControllerAppearanceCapture.shared.viewControllerDidAppear(self)
    }
}
#endif

