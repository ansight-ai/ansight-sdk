import Foundation

#if canImport(UIKit)
import UIKit
#endif

final class AnsightLifecycleObserver: @unchecked Sendable {
    static var isAvailable: Bool {
        #if canImport(UIKit)
        true
        #else
        false
        #endif
    }

    private let lock = NSLock()
    private weak var runtime: AnsightRuntime?
    private var options = AnsightLifecycleCaptureOptions.disabled
    private var lastScreenKey: String?
    private var lastScreenCapturedAt: Date?
    private var screenRouteResolver: AnsightScreenRouteResolver?

    #if canImport(UIKit)
    private var notificationObservers: [NSObjectProtocol] = []
    #endif

    func start(runtime: AnsightRuntime, options: AnsightLifecycleCaptureOptions) {
        var validatedOptions = options
        validatedOptions.validate()
        stop()

        guard validatedOptions.enabled else {
            return
        }

        lock.withLock {
            self.runtime = runtime
            self.options = validatedOptions
            self.lastScreenKey = nil
            self.lastScreenCapturedAt = nil
        }

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

        lock.withLock {
            runtime = nil
            options = .disabled
            lastScreenKey = nil
            lastScreenCapturedAt = nil
        }
    }

    func setScreenRouteResolver(_ resolver: AnsightScreenRouteResolver?) {
        lock.withLock {
            screenRouteResolver = resolver
            lastScreenKey = nil
            lastScreenCapturedAt = nil
        }
    }

    #if canImport(UIKit)
    @MainActor
    private func startOnMainActor() {
        let currentOptions = lock.withLock { options }
        guard currentOptions.enabled else {
            return
        }

        if currentOptions.captureAppLifecycle {
            installAppLifecycleObservers()
            captureCurrentApplicationState()
        }

        if currentOptions.captureScreenViews {
            AnsightViewControllerAppearanceCapture.shared.start(observer: self)
            captureCurrentVisibleScreen()
        }
    }

    @MainActor
    private func stopOnMainActor() {
        for observer in notificationObservers {
            NotificationCenter.default.removeObserver(observer)
        }
        notificationObservers.removeAll()
        AnsightViewControllerAppearanceCapture.shared.stop(observer: self)
    }

    @MainActor
    private func installAppLifecycleObservers() {
        let center = NotificationCenter.default
        notificationObservers.append(
            center.addObserver(
                forName: UIApplication.didBecomeActiveNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.recordLifecycleState(.foreground)
            }
        )
        notificationObservers.append(
            center.addObserver(
                forName: UIApplication.willEnterForegroundNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.recordLifecycleState(.foreground)
            }
        )
        notificationObservers.append(
            center.addObserver(
                forName: UIApplication.didEnterBackgroundNotification,
                object: nil,
                queue: .main
            ) { [weak self] _ in
                self?.recordLifecycleState(.background)
            }
        )
    }

    @MainActor
    private func captureCurrentApplicationState() {
        switch UIApplication.shared.applicationState {
        case .active, .inactive:
            recordLifecycleState(.foreground)
        case .background:
            recordLifecycleState(.background)
        @unknown default:
            break
        }
    }

    @MainActor
    private func captureCurrentVisibleScreen() {
        for scene in UIApplication.shared.connectedScenes {
            guard let windowScene = scene as? UIWindowScene,
                  windowScene.activationState == .foregroundActive ||
                    windowScene.activationState == .foregroundInactive
            else {
                continue
            }

            if let controller = windowScene.windows.first(where: { $0.isKeyWindow })?.rootViewController ??
                windowScene.windows.first(where: { !$0.isHidden })?.rootViewController {
                recordVisibleScreen(from: controller)
                return
            }
        }
    }

    func viewControllerDidAppear(_ viewController: UIViewController) {
        recordVisibleScreen(from: viewController)
    }

    private func recordVisibleScreen(from viewController: UIViewController) {
        let target = Self.visibleLeafViewController(from: viewController)
        guard !Self.shouldIgnore(target) else {
            return
        }

        let runtime = lock.withLock { self.runtime }
        let resolver = lock.withLock { self.screenRouteResolver }
        guard let runtime,
              let screen = Self.screenDescriptor(for: target, resolver: resolver)
        else {
            return
        }

        let now = Date()
        let shouldRecord = lock.withLock { () -> Bool in
            guard options.enabled, options.captureScreenViews else {
                return false
            }

            let minimumInterval = TimeInterval(options.minimumScreenViewIntervalMilliseconds) / 1_000.0
            if lastScreenKey == screen.key {
                return false
            }

            if let lastScreenCapturedAt,
               now.timeIntervalSince(lastScreenCapturedAt) < minimumInterval {
                return false
            }

            lastScreenKey = screen.key
            lastScreenCapturedAt = now
            return true
        }

        guard shouldRecord else {
            return
        }

        try? runtime.screenViewed(screen.name, details: screen.details)
    }

    private func recordLifecycleState(_ state: AppLifecycleState) {
        let runtime = lock.withLock { self.runtime }
        runtime?.setAppLifecycleState(state)
    }

    private static func visibleLeafViewController(from viewController: UIViewController) -> UIViewController {
        if let navigationController = viewController as? UINavigationController,
           let visibleViewController = navigationController.visibleViewController {
            return visibleLeafViewController(from: visibleViewController)
        }

        if let tabBarController = viewController as? UITabBarController,
           let selectedViewController = tabBarController.selectedViewController {
            return visibleLeafViewController(from: selectedViewController)
        }

        if let presentedViewController = viewController.presentedViewController,
           !presentedViewController.isBeingDismissed {
            return visibleLeafViewController(from: presentedViewController)
        }

        if let splitViewController = viewController as? UISplitViewController,
           let lastViewController = splitViewController.viewControllers.last {
            return visibleLeafViewController(from: lastViewController)
        }

        return viewController
    }

    private static func shouldIgnore(_ viewController: UIViewController) -> Bool {
        let className = String(describing: type(of: viewController))
        if className.hasPrefix("_") || className == "UIInputWindowController" {
            return true
        }

        let title = firstNonEmpty(
            viewController.navigationItem.title,
            viewController.title,
            viewController.tabBarItem.title
        )
        if title == nil, swiftUIHostingScreenName(for: viewController) != nil {
            return false
        }
        if title == nil,
           className == "UIViewController" || isAppleFrameworkViewController(viewController) {
            return true
        }

        return false
    }

    private static func screenDescriptor(
        for viewController: UIViewController,
        resolver: AnsightScreenRouteResolver?
    ) -> AnsightScreenDescriptor? {
        let className = String(describing: type(of: viewController))
        let reflectedClassName = String(reflecting: type(of: viewController))
        let title = firstNonEmpty(
            viewController.navigationItem.title,
            viewController.title,
            viewController.tabBarItem.title
        )
        let swiftUIName = swiftUIHostingScreenName(for: viewController)
        let name = title ?? swiftUIName ?? className
        let details: [String: String] = [
            "source": "uikit",
            "viewController": className,
            "viewControllerType": reflectedClassName,
        ]
        let defaultDescriptor = AnsightScreenDescriptor(name: name, key: "\(className):\(name)", details: details)
        let context = AnsightScreenRouteContext(
            source: "uikit",
            defaultName: name,
            defaultKey: defaultDescriptor.key,
            title: title,
            viewControllerName: className,
            viewControllerTypeName: reflectedClassName,
            swiftUIRootTypeName: swiftUIName,
            details: details
        )
        return AnsightScreenRouteResolution.resolve(
            defaultDescriptor: defaultDescriptor,
            context: context,
            resolver: resolver
        )
    }

    private static func firstNonEmpty(_ values: String?...) -> String? {
        for value in values {
            let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            if !trimmed.isEmpty {
                return trimmed
            }
        }
        return nil
    }

    private static func isAppleFrameworkViewController(_ viewController: UIViewController) -> Bool {
        let bundleIdentifier = Bundle(for: type(of: viewController)).bundleIdentifier ?? ""
        return bundleIdentifier == "com.apple.UIKitCore" ||
            bundleIdentifier == "com.apple.UIKit" ||
            bundleIdentifier.hasPrefix("com.apple.")
    }

    private static func swiftUIHostingScreenName(for viewController: UIViewController) -> String? {
        let reflectedClassName = String(reflecting: type(of: viewController))
        guard reflectedClassName.contains("UIHostingController") else {
            return nil
        }

        guard let genericStart = reflectedClassName.firstIndex(of: "<"),
              let genericEnd = reflectedClassName.lastIndex(of: ">"),
              genericStart < genericEnd
        else {
            return "SwiftUI"
        }

        let genericType = String(reflectedClassName[reflectedClassName.index(after: genericStart)..<genericEnd])
        return simplifiedSwiftUITypeName(genericType) ?? "SwiftUI"
    }

    private static func simplifiedSwiftUITypeName(_ typeName: String) -> String? {
        let ignoredPrefixes = [
            "SwiftUI.ModifiedContent",
            "SwiftUI.Optional",
            "Swift.Optional",
            "SwiftUI.AnyView",
        ]
        let normalized = typeName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else {
            return nil
        }

        for prefix in ignoredPrefixes where normalized.hasPrefix(prefix) {
            if let nested = firstGenericArgument(in: normalized),
               let simplified = simplifiedSwiftUITypeName(nested) {
                return simplified
            }
        }

        let firstArgument = firstGenericArgument(in: normalized)
        if normalized.hasPrefix("SwiftUI.TupleView"),
           let firstArgument,
           let simplified = simplifiedSwiftUITypeName(firstArgument) {
            return simplified
        }

        let candidates = [normalized, firstArgument].compactMap { $0 }
        for candidate in candidates {
            let components = candidate
                .split(whereSeparator: { character in
                    character == "." || character == "<" || character == "," || character == " "
                })
                .map(String.init)
            if let appTypeName = components.last(where: { component in
                !component.isEmpty &&
                    !component.hasPrefix("_") &&
                    component != "SwiftUI" &&
                    component != "View" &&
                    component != "ModifiedContent" &&
                    component != "TupleView"
            }) {
                return appTypeName
            }
        }

        return nil
    }

    private static func firstGenericArgument(in typeName: String) -> String? {
        guard let start = typeName.firstIndex(of: "<") else {
            return nil
        }

        var depth = 0
        var argumentStart: String.Index?
        var index = typeName.index(after: start)
        while index < typeName.endIndex {
            let character = typeName[index]
            if argumentStart == nil, !character.isWhitespace {
                argumentStart = index
            }

            if character == "<" {
                depth += 1
            } else if character == ">" {
                if depth == 0 {
                    guard let argumentStart else {
                        return nil
                    }
                    return String(typeName[argumentStart..<index])
                }
                depth -= 1
            } else if character == "," && depth == 0 {
                guard let argumentStart else {
                    return nil
                }
                return String(typeName[argumentStart..<index])
            }

            index = typeName.index(after: index)
        }

        return nil
    }
    #endif
}
