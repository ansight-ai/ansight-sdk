---
title: iOS Native Setup
description: Configure the Ansight native Swift SDK, developer pairing, remote tools, screen capture, FPS capture, and touch capture for iOS apps.
---

Use the aggregate `Ansight` SwiftPM product for native iOS developer builds. It includes the core runtime, host pairing, screen and touch capture, FPS telemetry, and the current native remote-tool suites.

Add the Swift package from the Ansight SDK `src/ios` package and select the `Ansight` product for the app target. The package supports iOS 15 and newer.

```swift
import SwiftUI
import Ansight

@main
struct ExampleApp: App {
    init() {
        #if DEBUG
        do {
            try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
        } catch {
            assertionFailure("Failed to start Ansight: \(error)")
        }
        #endif
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
```

For UIKit apps, call the same setup during application startup:

```swift
import UIKit
import Ansight

@main
final class AppDelegate: UIResponder, UIApplicationDelegate {
    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
    ) -> Bool {
        #if DEBUG
        do {
            try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
        } catch {
            assertionFailure("Failed to start Ansight: \(error)")
        }
        #endif

        return true
    }
}
```

`initializeAndActivateAnsightSdk()` applies the native developer defaults and registers the bundled tool suites:

- telemetry sampling every 400 ms with 120 seconds of retained samples
- automatic UIKit lifecycle, UIKit screen-view capture, and SwiftUI `UIHostingController` screen-view capture
- FPS telemetry through UIKit `CADisplayLink`
- JPEG screen streaming every 2 seconds at quality 60 and max width 480
- UIKit touch capture with tap, move, and cancellation events
- full remote-tool access through `AnsightToolGuard.fullAccess`
- host auto-probe using bundled or saved pairing configuration
- preferences, filesystem, database, secure storage, and visual-tree tools

Do not ship the aggregate developer setup or full tool access in production builds. Remote tools can expose screenshots, UI state, app sandbox files, SQLite data, preferences, Keychain values allowed by configuration, and diagnostic overlay controls to a connected host.

## Developer pairing

The SwiftPM build plugin can embed a Studio pairing config into the SDK at build time. Enable it for local developer builds by setting these environment variables for the app build:

```bash
export ANSIGHT_DEVELOPER_PAIRING_ENABLED=true
export ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE=/absolute/path/to/ansight.json
export ANSIGHT_ALLOW_REMOTE_TOOLS=true
```

`ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE` should point at a pairing config issued by Ansight Studio for the target app. If it is omitted, the plugin looks for `src/ios/ansight.json` in the SDK package checkout.

`ANSIGHT_ALLOW_REMOTE_TOOLS=true` is required when the build includes concrete `AnsightTool` implementations. Without it, the SwiftPM build plugin fails the build so remote tools are not included accidentally.

With developer pairing embedded, activation can connect automatically when Studio is available. To force an explicit connection from app code, disable auto-probe and call `connect(...)` yourself:

```swift
import Ansight

var options = AnsightOptions.ansightDeveloperDefaults
options.hostAutoProbe = .disabledDefault

try AnsightRuntime.shared.initializeAndActivateAnsightSdk(options: options)

Task {
    let result = await AnsightRuntime.shared.connect(.auto(clientName: "Example iOS App"))
    if !result.success {
        print("Ansight connection failed: \(result.message)")
    }
}
```

For tests that inject one explicit pairing file, pass it as the bundled host connection and keep auto-probe disabled for deterministic startup:

```swift
import Ansight

let pairingJson = """
{ ... Studio pairing config JSON ... }
"""

let options = AnsightOptions(
    sessionJpegCapture: AnsightSessionJpegCaptureOptions(
        intervalMilliseconds: 2_000,
        quality: 60,
        maxWidth: 480
    ),
    touchCapture: AnsightTouchCaptureOptions(),
    toolGuard: .fullAccess,
    hostAutoProbe: .disabledDefault,
    hostConnection: AnsightHostConnectionOptions(
        bundledConfigJson: pairingJson
    )
)

try AnsightRuntime.shared.initializeAndActivateAnsightSdk(options: options)

Task {
    _ = await AnsightRuntime.shared.connect(.bundledConfig(clientName: "Example iOS App"))
}
```

## Fine-grained setup

Use `AnsightKit` directly when the app should own which tools and captures are enabled:

```swift
import AnsightKit
import AnsightToolsDatabase
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsSecureStorage
import AnsightToolsVisualTree

try AnsightRuntime.shared.initializeAndActivate(
    options: AnsightOptions(
        enableFramesPerSecond: true,
        lifecycleCapture: AnsightLifecycleCaptureOptions(
            captureAppLifecycle: true,
            captureScreenViews: true
        ),
        sessionJpegCapture: AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: 1_000,
            quality: 70,
            maxWidth: 960
        ),
        touchCapture: AnsightTouchCaptureOptions(),
        toolGuard: .readOnly
    )
)

try AnsightRuntime.shared.registerPreferencesTools()
try AnsightRuntime.shared.registerFileSystemTools()
try AnsightRuntime.shared.registerDatabaseTools()
try AnsightRuntime.shared.registerSecureStorageTools(
    options: AnsightSecureStorageToolsOptions(
        allowedKeyPrefixes: ["ansight.debug."]
    )
)
try AnsightRuntime.shared.registerVisualTreeTools()
```

`AnsightToolGuard.disabled` hides and blocks tool execution. `AnsightToolGuard.readOnly` allows read-scoped tools. `AnsightToolGuard.fullAccess` allows read, write, and delete scoped tools and should stay limited to trusted developer builds.

## Current app signals

The developer defaults install UIKit lifecycle and view-controller appearance hooks so foreground/background state, UIKit screen-view events, and SwiftUI `UIHostingController` root view names are reported automatically. Apps with custom routers can install a route resolver before activation:

```swift
AnsightRuntime.shared.setScreenRouteResolver(
    AnsightScreenRouteResolver { context in
        guard context.swiftUIRootTypeName == "RootView" else {
            return nil
        }

        return AnsightScreenRoute(
            name: "Orders",
            key: "route:/orders",
            details: [
                "route": "/orders",
                "defaultScreen": context.defaultName
            ]
        )
    }
)

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
```

Returning `nil` or a blank route name keeps the default screen descriptor. Apps that need manual reporting can opt out:

```swift
var options = AnsightOptions.ansightDeveloperDefaults
options.lifecycleCapture = .disabled

try AnsightRuntime.shared.initializeAndActivateAnsightSdk(options: options)
```

Manual lifecycle and screen-view calls are still available for non-UIKit routing layers or apps that disabled automatic capture:

```swift
AnsightRuntime.shared.setAppLifecycleState(.foreground)
try AnsightRuntime.shared.screenViewed("Orders")
```

Call `setAppLifecycleState(...)` from scene or application lifecycle callbacks and call `screenViewed(...)` when the visible screen changes.

## Validate with test apps

Use the repository validation helper against the native iOS test app corpus:

```bash
cd /Users/matthewrobbins/Development/git/ansight-sdk

python3 scripts/validate_ios_test_apps.py \
  --test-apps-root /Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios \
  --app CleanStore \
  --simulator <booted-simulator-udid>
```

The helper injects a local Swift package reference to `src/ios`, generates a bootstrap file, prepares an app-specific pairing config, builds, installs, and launches the selected app. It intentionally writes generated integration files into each selected test app checkout.

For one explicit Studio pairing config:

```bash
python3 scripts/validate_ios_test_apps.py \
  --test-apps-root /Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios \
  --app CleanStore \
  --pairing-config /absolute/path/to/ansight.json \
  --simulator <booted-simulator-udid>
```

For repeatable Ansight Studio-backed validation, let the helper issue fresh
Studio configs and verify the launched live session:

```bash
python3 scripts/validate_ios_test_apps.py \
  --test-apps-root /Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios \
  --app CleanStore \
  --simulator <booted-simulator-udid> \
  --studio-issue-configs \
  --studio-verify
```

To make app-icon validation deterministic on a test app that does not already
compile usable icon assets, inject a validation icon and require Studio to record
it in the session device profile:

```bash
python3 scripts/validate_ios_test_apps.py \
  --test-apps-root /Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios \
  --app CleanStore \
  --simulator <booted-simulator-udid> \
  --inject-validation-app-icon \
  --studio-issue-configs \
  --studio-verify \
  --studio-require-icon
```

To also prove Studio received the native device profile runtime, coarse
network, GPU/render-backend, environment, and privacy-safe profile fields, add
the device-profile gate:

```bash
python3 scripts/validate_ios_test_apps.py \
  --test-apps-root /Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios \
  --app CleanStore \
  --simulator <booted-simulator-udid> \
  --studio-issue-configs \
  --studio-verify \
  --studio-require-device-profile-details
```

To prove custom automatic route naming, inject the validation route resolver and
require Studio logs to include the custom screen-view route:

```bash
python3 scripts/validate_ios_test_apps.py \
  --test-apps-root /Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios \
  --app CleanStore \
  --simulator <booted-simulator-udid> \
  --studio-issue-configs \
  --studio-verify \
  --inject-validation-route-resolver \
  --studio-require-validation-route \
  --studio-min-tools 28
```

Studio verification writes the issued config id, live session id, WebSocket
status, metric sample count, FPS sample count, screenshot count, remote tool
count, known-app icon path when present, session app-icon sync status,
device-profile detail status, and validation route visibility to
`.ansight-validation/ios-test-app-validation-results.json`.

After launch, validate the session in Ansight Studio with the live app session:

- confirm `DeviceAppProfile` is present and includes app metadata and app icon when the target app bundle contains icon assets
- call `ui.get_screenshot` and confirm modal overlays, input views, pickers, and sheets are visible
- call `ui.get_visual_tree`, `ui.inspect_node`, and overlay tools for UIKit hierarchy coverage
- confirm FPS samples arrive on the reserved FPS metric channel
- confirm touch batches arrive while tapping, dragging, and cancelling touches
- exercise preferences, filesystem, database, and secure-storage tools only against data the test app is allowed to expose

Run SDK unit tests from the Swift package after changing SDK code:

```bash
cd /Users/matthewrobbins/Development/git/ansight-sdk/src/ios
swift test
```
