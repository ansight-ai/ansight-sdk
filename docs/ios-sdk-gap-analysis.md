# iOS SDK Gap Analysis

Date: 2026-06-14

## Current Implemented Surface

- Core runtime initialization, activation, clearing, manual and automatic UIKit lifecycle state, screen views, metrics, events, and debug snapshots.
- Pairing config parsing for `ansight.pairing-config.v1`, `ansight.pairing-config-document.v1`, and legacy `ansight.pairing-ticket.v1`.
- UDP bootstrap, WebSocket session handoff, remembered host profiles with saved/cached status separation, Wi-Fi-keyed cached profile refresh, legacy cached-profile migration, newest-first cached profile resolution, automatic live retry across resolved host candidates, app-provided file/QR config reader entry points, SDK-owned UIKit document picker and QR scanner, metric channel announcement, sequence-cursored retained metric/event streaming, app state streaming, clean disconnect, and host status snapshots.
- Automatic UIKit foreground/background capture, UIKit view-controller and SwiftUI `UIHostingController` screen-view capture with opt-out controls, app-provided route naming hooks, FPS capture through `CADisplayLink`, live JPEG screen capture, and UIKit touch capture.
- Tool protocol bridge with `tool.query`, `tool.catalog`, `tool.call`, guard policy, security metadata, case-insensitive IDs, duplicate rejection, live binary transfer queueing, host acknowledgement/send timeout coverage, and sustained large-transfer frame/chunk-boundary coverage.
- Device/app profile collection with SDK/app/build/process metadata, app icon payloads, Apple device/OS/locale/time-zone/CPU/memory/storage/display/battery/thermal metadata, Swift/runtime stack codes, Metal GPU/render-backend metadata, and privacy-safe coarse network path metadata.
- SwiftPM products:
  - `AnsightKit`
  - aggregate `Ansight`
  - `AnsightToolsPreferences`
  - `AnsightToolsFileSystem`
  - `AnsightToolsDatabase`
  - `AnsightToolsSecureStorage`
  - `AnsightToolsVisualTree`
- SwiftPM build plugin for developer pairing artifacts and bundled remote-tool policy enforcement.
- Native iOS test-app validator that can issue fresh Ansight Studio pairing configs through the Studio MCP daemon, install/launch simulator apps, inject deterministic validation app icons, inject route resolver validation hooks, and verify live Studio session evidence for metrics, FPS, screenshots, remote-tool catalog, session-recorded app-icon sync status, and custom automatic screen-view routes.
- Native harness validated against Ansight Studio with live telemetry, screenshot, visual tree catalog, and database tool calls.
- CleanStore test app validated against Ansight Studio with automatic `ListOrdersViewController` screen-view capture, foreground lifecycle state, FPS samples, screenshots, a 28-tool catalog, and telemetry/FPS streaming beyond the developer retention buffer in sessions `com-clean-swift-cleanstore-520` and `com-clean-swift-cleanstore-523`.
- SwiftUI2048 test app validated against Ansight Studio with automatic `GameView` screen-view capture from `UIHostingController`, foreground lifecycle state, FPS samples, and screenshot capture in session `com-cyandev-swiftui2048-522`.

## Remaining Full-Spec Gaps

1. Host connection parity:
   - Saved config state, cached profile state, .NET precedence order, Wi-Fi-keyed cached profile refresh, legacy cached-profile migration, newest-first cached profile resolution, live retry across multiple cached/saved/bundled candidates, stale cached-profile cleanup, app-provided file/QR config reader entry points, and SDK-owned UIKit document picker / QR scanner support are implemented.
   - Remaining host pairing polish is deeper device/simulator validation of the picker/scanner UI paths.

2. Lifecycle integration polish:
   - UIKit lifecycle, UIKit view-controller screen capture, SwiftUI `UIHostingController` screen capture, opt-out controls, and app-provided route naming hooks are implemented.
   - Remaining lifecycle work is broader corpus validation across apps with custom SwiftUI routers once those samples build on the current Xcode/Swift toolchain.

3. Device/app profile depth:
   - Core profile parity is implemented for available non-PII Apple APIs, including runtime codes, Metal GPU/render backend, coarse network transport, app icon profile serialization, bundle collection, deterministic test-app icon injection, and Studio session icon verification.
   - Remaining depth is permission-relevant facts where native APIs allow them without PII, and richer network or hardware data only after explicit privacy/security review.

4. Tool infrastructure polish:
   - Basic protocol-boundary validation now rejects non-object `arguments` payloads, rejects non-object `payload` values, preserves JSON object/array/scalar arguments as tool strings, ignores unsupported capabilities, and covers unknown tool protocol types for .NET parity.
   - Host acknowledgement timeout, WebSocket send timeout, sustained large-transfer framing, and runtime chunk-size clamp tests are implemented.
   - Remaining tool infrastructure validation is a live Studio large-binary reassembly run through `files.begin_binary_download`.

5. Reflection tools:
   - Still intentionally blocked pending native security and object-model design approval.
   - Do not port .NET reflection directly.

6. Distribution:
   - CocoaPods support is not implemented.
   - Release packaging/signing/versioning metadata needs a pass before external consumption.

7. Cross-app validation:
   - Native harness is validated live in Ansight Studio.
   - The native iOS suite under `/Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios` currently inventories as 57 app projects after excluding generated build output, dependency folders, and duplicate nested slugs.
   - The repeatable validation harness can add the SDK, issue Studio configs, register selected tools, build, install, launch, and query Studio for each app.
   - Full-suite completion is still outstanding: many checked-out samples fail before SDK runtime validation because of missing CocoaPods support files, unresolved package/module dependencies, stale SwiftUI APIs, or `xcodebuild -showBuildSettings` timeouts.

## Native iOS Test-App Coverage Targets

High-priority database/secure-storage apps:

- `devxoul__SwiftUITodo`: preferences, database, SwiftUI state workflows.
- `horizontalsystems__unstoppable-wallet-ios`: preferences, database, secure storage, dense real-world app.
- `mattrubin__authenticator`: preferences, database, secure storage.
- `mssun__passforios`: preferences, database, secure storage.

High-priority filesystem apps:

- `Ranchero-Software__NetNewsWire`
- `amitburst__HackerNews`
- `glushchenko__fsnotes`
- `tnantoka__edhita`
- `wikimedia__wikipedia-ios`

High-priority FPS/touch/screenshot apps:

- `newlinedotco__FlappySwift`
- `austinzheng__swift-2048`
- `unixzii__SwiftUI-2048`
- `AudioKit__AudioKitSynthOne`
- `analogcode__Swift-Radio-Pro`

Broad visual-tree apps:

- `Clean-Swift__CleanStore`
- `Finb__Bark`
- `hilen__TSWeChat`
- `ivanvorobei__SwiftUI`
- `jordansinger__SwiftUI-Kit`
- `jordibruin__Swift-Charts-Examples`
- `mainframecomputer__fullmoon-ios`
- `ml-explore__mlx-swift-examples`

## Latest Validation Evidence

- `swift test` in `src/ios`: 75 tests, 1 skipped, 0 failures.
- Native unit coverage now verifies host acknowledgement timeout, WebSocket send timeout closure, sustained binary transfer frame headers/chunk offsets, payload reconstruction, and runtime chunk-size clamps.
- Native harness Xcode build succeeded for iPhone 17e iOS 26.4 simulator.
- Ansight Studio live session `ai-ansight-ios-native-harness-510` reported WebSocket Open and a 28-tool catalog.
- Studio `data.list_databases` found `Documents/ansight-harness/sample.sqlite`.
- Studio `data.describe_schema` returned `harness_events` schema.
- Studio `data.query` returned typed rows, blob descriptors, column metadata, and truncation metadata.
- Studio rejected `DELETE FROM harness_events` through `data.query` with `database_query_failed`.
- `Clean-Swift__CleanStore` installed and launched through `scripts/validate_ios_test_apps.py` on iPhone 17e iOS 26.4 with config `b03423ac61a44ea4839792e4dc363d58`.
- Ansight Studio live session `com-clean-swift-cleanstore-520` reported WebSocket Open, foreground lifecycle state, automatic `List Orders` screen-view capture from `ListOrdersViewController`, FPS samples, JPEG screenshot capture, and a 28-tool catalog.
- `Clean-Swift__CleanStore` relaunched with config `9bfe51ea424b4eb28a711e6a01696234` after the retained telemetry cursor fix; Ansight Studio live session `com-clean-swift-cleanstore-523` stayed WebSocket Open, reached `metricSampleCount: 689`, and continued receiving FPS samples through `2026-06-14T04:42:47Z`, beyond the previous 360-sample developer retention cap.
- `Clean-Swift__CleanStore` relaunched with config `94d12d40ff9147fc99ca9aec6b03050d` after the WebSocket send-timeout and reconnect fix; Ansight Studio live session `com-clean-swift-cleanstore-535` stayed WebSocket Open, reached `metricSampleCount: 586`, and continued receiving FPS and physical-footprint samples through `2026-06-14T05:24:49Z`.
- `Clean-Swift__CleanStore` relaunched with injected validation app icon and config `376e847f7da44d00adb981a3a341bfa3`; Ansight Studio live session `com-clean-swift-cleanstore-537` recorded the session device-profile app icon as a `120x120` PNG with `2033` encoded bytes, while also reporting WebSocket Open, FPS samples, screenshot capture, and the 28-tool catalog.
- `Clean-Swift__CleanStore` relaunched with an injected validation route resolver and config `98fe55033d8c4d6ea02b5a27d184e2e3`; Ansight Studio live session `com-clean-swift-cleanstore-538` reported WebSocket Open, 33 metric samples, 22 FPS samples, 1 screenshot, a 28-tool catalog, and a `SCREENVIEWED` log for `Ansight SDK Validation Route` with `route=/ansight-validation` and default source `List Orders`.
- `Clean-Swift__CleanStore` relaunched with injected validation app icon, injected route resolver, and config `6c4a883a1ce242b39af753454d2821d8`; Ansight Studio live session `com-clean-swift-cleanstore-539` reported WebSocket Open, 32 metric samples, 22 FPS samples, 1 screenshot, a 28-tool catalog, a session app icon, the validation route, and device profile details: runtime code `2`, network transport code `2`, GPU API code `3`, render backend code `3`, environment code `3`, and privacy-safe profile JSON.
- `Clean-Swift__CleanStore` relaunched after the remembered-profile collection change with config `066d1f1586bc4804b1271c0e6d997582`; Ansight Studio live session `com-clean-swift-cleanstore-540` reported WebSocket Open, 27 metric samples, 22 FPS samples, 1 screenshot, a 28-tool catalog, a session app icon, the validation route, and device profile detail sync.
- `Clean-Swift__CleanStore` relaunched after automatic host-candidate retry support with config `8448f22da56c46cc846ebe6fb0c86e7b`; Ansight Studio live session `com-clean-swift-cleanstore-541` reported WebSocket Open, 27 metric samples, 22 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync, the validation route, and device profile detail sync.
- `Clean-Swift__CleanStore` relaunched after app-provided file/QR config reader support with config `5bf38c6bec57400c8182cbd91348210e`; Ansight Studio live session `com-clean-swift-cleanstore-542` reported WebSocket Open, 33 metric samples, 23 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync, the validation route, and device profile detail sync.
- `Clean-Swift__CleanStore` relaunched after SDK-owned UIKit file/QR reader support with config `74cc0e67b9384db2a0a683fac56c701a`; Ansight Studio live session `com-clean-swift-cleanstore-543` reported WebSocket Open, 28 metric samples, 20 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync, the validation route, and device profile detail sync.
- Studio-backed validator smoke run with fixed unique slugs verified:
  - `Clean-Swift__CleanStore`: session `com-clean-swift-cleanstore-532`, WebSocket Open, 21 metric samples, 18 FPS samples, 1 screenshot, 28 tools.
  - `austinzheng__swift-2048`: session `f3nghuang-swift-2048-533`, WebSocket Open, 23 metric samples, 19 FPS samples, 1 screenshot, 28 tools.
  - `ivanvorobei__SwiftUI__Other-Projects__Animating-Views-And-Transitions__Complete__Landmarks`: session `com-example-apple-samplecode-landmarks-534`, WebSocket Open, 7 metric samples, 4 FPS samples, 1 screenshot, 28 tools.
- A partial broad corpus run processed 18 rows before interruption to fix harness slug/output-root behavior: 3 Studio-verified apps, 14 pre-runtime app build/resolution failures, and 1 interrupted pending row.
- Common pre-runtime failures observed in the corpus include missing CocoaPods xcconfig files (`AudioKit__AudioKitSynthOne`, `Finb__Bark`, `JakeLin__SwiftLanguageWeather`, `amitburst__HackerNews`, `hilen__TSWeChat`), unresolved module dependency `Popovers` (`aheze__OpenFind`), stale SwiftUI APIs (`devxoul__SwiftUITodo`), unresolved bundle id (`coderyi__Monkey`), and `xcodebuild -showBuildSettings` timeouts (`analogcode__Swift-Radio-Pro`, `glushchenko__fsnotes`, `horizontalsystems__unstoppable-wallet-ios`).
- `unixzii__SwiftUI-2048` installed and launched through `scripts/validate_ios_test_apps.py` on iPhone 17e iOS 26.4 with config `99de3dd6f3824fb6ac5b6771de51c2a9`.
- Ansight Studio live session `com-cyandev-swiftui2048-522` reported WebSocket Open, foreground lifecycle state, automatic `GameView` screen-view capture from `UIHostingController<ModifiedContent<GameView, ...>>`, FPS samples, and JPEG screenshot capture.
- `devxoul__SwiftUITodo` currently fails before SDK validation on the current Xcode/Swift toolchain because the app source still uses removed SwiftUI APIs such as `@propertyDelegate` and `BindableObject`.
