#if canImport(UIKit)
import AnsightCore
import UIKit

enum PlatformPairingPresenter {
    @MainActor
    static func presentingViewController() throws -> UIViewController {
        for scene in UIApplication.shared.connectedScenes {
            guard let windowScene = scene as? UIWindowScene else {
                continue
            }

            let window = windowScene.windows.first { $0.isKeyWindow } ?? windowScene.windows.first
            if let rootViewController = window?.rootViewController {
                return resolvePresentedController(rootViewController)
            }
        }

        if let keyWindow = UIApplication.shared.windows.first(where: { $0.isKeyWindow }),
           let rootViewController = keyWindow.rootViewController {
            return resolvePresentedController(rootViewController)
        }

        throw RuntimeError.invalidInput("Pairing UI is unavailable because no active iOS view controller is available.")
    }

    @MainActor
    private static func resolvePresentedController(_ controller: UIViewController) -> UIViewController {
        var current = controller
        while true {
            if let presented = current.presentedViewController {
                current = presented
                continue
            }

            if let navigation = current as? UINavigationController,
               let visible = navigation.visibleViewController {
                current = visible
                continue
            }

            if let tab = current as? UITabBarController,
               let selected = tab.selectedViewController {
                current = selected
                continue
            }

            return current
        }
    }
}
#endif
