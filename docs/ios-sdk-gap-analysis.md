# iOS SDK Gap Analysis

Date: 2026-06-14

## Current Implemented Surface

- Core runtime initialization, activation, clearing, manual and automatic UIKit lifecycle state, screen views, metrics, events, and debug snapshots.
- Pairing config parsing for `ansight.pairing-config.v1`, `ansight.pairing-config-document.v1`, and legacy `ansight.pairing-ticket.v1`.
- UDP bootstrap, WebSocket session handoff, remembered host profiles with saved/cached status separation, Wi-Fi-keyed cached profile refresh, legacy cached-profile migration, newest-first cached profile resolution, automatic live retry across resolved host candidates, app-provided file/QR config reader entry points, SDK-owned UIKit document picker and QR scanner, metric channel announcement, sequence-cursored retained metric/event streaming, app state streaming, clean disconnect, and host status snapshots.
- Automatic UIKit foreground/background capture, UIKit view-controller and SwiftUI `UIHostingController` screen-view capture with opt-out controls, app-provided route naming hooks, FPS capture through `CADisplayLink`, live JPEG screen capture, and UIKit touch capture.
- Tool protocol bridge with `tool.query`, `tool.catalog`, `tool.call`, guard policy, security metadata, case-insensitive IDs, duplicate rejection, live binary transfer queueing, host acknowledgement/send timeout coverage, and sustained large-transfer frame/chunk-boundary coverage.
- Device/app profile collection with SDK/app/build/process metadata, app icon payloads, Apple device/OS/locale/time-zone/CPU/memory/storage/display/battery/thermal metadata, Swift/runtime stack codes, Metal GPU/render-backend metadata, and privacy-safe coarse network path metadata.
- SwiftPM products and matching local CocoaPods podspecs:
  - `AnsightCore`
  - `AnsightPairingQR`
  - aggregate `Ansight`
  - `AnsightToolsPreferences`
  - `AnsightToolsFileSystem`
  - `AnsightToolsDatabase`
  - `AnsightToolsSecureStorage`
  - `AnsightToolsVisualTree`
- SwiftPM build plugin and CocoaPods `AnsightCore` script phase for developer pairing artifacts and bundled remote-tool policy enforcement; UIKit file/QR pairing UI is isolated in `AnsightPairingQR`.
- Native iOS test-app validator that can issue fresh Ansight Studio pairing configs through the Studio MCP daemon, prefer iOS targets/schemes in multi-platform projects, optionally run `pod install` for CocoaPods apps, force x86_64 simulator destinations with an arm64 exclusion override for older binary pods, relax warning-heavy legacy dependency builds when requested, override the simulator deployment target for apps that require newer APIs, optionally skip unavailable Xcode scheme actions and trusted package-plugin/macro validation prompts, install/launch simulator apps, inject deterministic validation app icons, inject route resolver validation hooks, inject validation-only synthetic touch probes and picker-style input overlays with first-responder retry/fallback handling, seed deterministic validation files, tear down timed-out Xcode command process groups, verify live Studio session evidence for metrics, FPS, screenshots, remote-tool catalog, session-recorded app-icon sync status, custom automatic screen-view routes, captured touch input, input-overlay screenshot pixels, and `files.begin_binary_download` metadata for a chunked validation payload, write an aggregate summary artifact for corpus-level gate/failure triage, and merge/skip prior verified rows for resumable full-corpus validation.
- Native harness validated against Ansight Studio with live telemetry, screenshot, visual tree catalog, and database tool calls.
- CleanStore test app validated against Ansight Studio with automatic `ListOrdersViewController` screen-view capture, foreground lifecycle state, FPS samples, screenshots, a 28-tool catalog, and telemetry/FPS streaming beyond the developer retention buffer in sessions `com-clean-swift-cleanstore-520` and `com-clean-swift-cleanstore-523`.
- SwiftUI2048 test app validated against Ansight Studio with automatic `GameView` screen-view capture from `UIHostingController`, foreground lifecycle state, FPS samples, and screenshot capture in session `com-cyandev-swiftui2048-522`.

## Remaining Full-Spec Gaps

1. Host connection parity:
   - Saved config state, cached profile state, .NET precedence order, Wi-Fi-keyed cached profile refresh, legacy cached-profile migration, newest-first cached profile resolution, live retry across multiple cached/saved/bundled candidates, stale cached-profile cleanup, app-provided file/QR config reader entry points, and SDK-owned UIKit document picker / QR scanner support are implemented.
   - Remaining host pairing polish is deeper device/simulator validation of the picker/scanner UI paths.

2. Lifecycle integration polish:
   - UIKit lifecycle, UIKit view-controller screen capture, SwiftUI `UIHostingController` screen capture, opt-out controls, app-provided route naming hooks, and UIKit touch capture are implemented.
   - SDK touch streaming and Studio ingestion are now live-validated through a validation-only Swift SPI that emits a deterministic down/up pair after the test app opens a live session.
   - Picker-style `UITextField.inputView` overlay capture is now live-validated by a Studio screenshot-frame pixel assertion.
   - Remaining lifecycle work is broader corpus validation across apps with custom SwiftUI routers once those samples build on the current Xcode/Swift toolchain, plus deeper physical simulator/device gesture validation because the current simulator command-line tooling does not expose a reliable tap primitive and local macOS click injection did not produce a new Studio touch payload.

3. Device/app profile depth:
   - Core profile parity is implemented for available non-PII Apple APIs, including runtime codes, Metal GPU/render backend, coarse network transport, app icon profile serialization, bundle collection, deterministic test-app icon injection, and Studio session icon verification.
   - Remaining depth is permission-relevant facts where native APIs allow them without PII, and richer network or hardware data only after explicit privacy/security review.

4. Tool infrastructure polish:
   - Basic protocol-boundary validation now rejects non-object `arguments` payloads, rejects non-object `payload` values, preserves JSON object/array/scalar arguments as tool strings, ignores unsupported capabilities, and covers unknown tool protocol types for .NET parity.
   - Host acknowledgement timeout, WebSocket send timeout, sustained large-transfer framing, and runtime chunk-size clamp tests are implemented.
   - Studio-backed metadata validation for `files.begin_binary_download` is implemented and passing for a 150 KB validation file.
   - Remaining tool infrastructure validation is a live Studio large-binary reassembly run once the host bridge exposes the completed artifact path for generic `files.begin_binary_download` transfers.

5. Reflection tools:
   - Still intentionally blocked pending native security and object-model design approval.
   - Do not port .NET reflection directly.

6. Distribution:
   - Local CocoaPods podspecs now mirror the SwiftPM product set, `AnsightPairingQR` carries the UIKit/AVFoundation pairing UI, the `AnsightCore` pod runs the developer build-artifact generator before compile, and local aggregate lint passes.
   - Remaining distribution work is public release source metadata, signing/versioning, and external publication validation.

7. Cross-app validation:
   - Native harness is validated live in Ansight Studio.
   - The native iOS suite under `/Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios` currently inventories as 57 app projects after excluding generated build output, dependency folders, and duplicate nested slugs.
   - The repeatable validation harness can add the SDK, issue Studio configs, register selected tools, run CocoaPods dependency install when requested, build, install, launch, query Studio for each app, and write `ios-test-app-validation-summary.json` with verified apps, failure categories, SDK-runtime reachability, and Studio feature-gate counts.
   - Broad runs can now resume from a prior `ios-test-app-validation-results.json` with `--merge-results` and skip previously Studio-verified apps with `--skip-verified-results`, so full-suite evidence can be accumulated across multiple shorter runs.
   - Full-suite completion is still outstanding: many checked-out samples fail before SDK runtime validation because of missing CocoaPods support files, unresolved package/module dependencies, stale SwiftUI APIs, or `xcodebuild -showBuildSettings` timeouts.

## Native iOS Test-App Coverage Targets

High-priority database/secure-storage apps:

- `devxoul__SwiftUITodo`: preferences, database, SwiftUI state workflows.
- `horizontalsystems__unstoppable-wallet-ios`: preferences, database, secure storage, dense real-world app.
- `mattrubin__authenticator`: preferences, database, secure storage; currently resolves to a macOS-only scheme in this checkout.
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

- `swift test` in `src/ios`: 76 tests, 1 skipped, 0 failures.
- Native unit coverage now verifies host acknowledgement timeout, WebSocket send timeout closure, sustained binary transfer frame headers/chunk offsets, payload reconstruction, and runtime chunk-size clamps.
- `Clean-Swift__CleanStore` relaunched with the Studio binary-download probe and config `22c97980037945d58c04dddc53697b7f`; Ansight Studio live session `com-clean-swift-cleanstore-544` reported WebSocket Open, 34 metric samples, 29 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync, the validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `e70809b173a144c38f6513bdf533b9e3`. The current Studio MCP response did not include an artifact path, so host-side reassembly remains unverified.
- Studio-backed validator smoke run with app-icon, validation-route, device-profile, and binary-download probes verified:
  - `austinzheng__swift-2048`: config `6b4c468c884d401b91a2565047f50f2e`, session `f3nghuang-swift-2048-545`, WebSocket Open, 17 metric samples, 10 FPS samples, 1 screenshot, 28 tools, app icon sync, validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `2fe93bfee4da433899c4eeebfe92e847`.
  - `ivanvorobei__SwiftUI__Other-Projects__Animating-Views-And-Transitions__Complete__Landmarks`: config `79fe7cc467904bb7b34c453761bf4845`, session `com-example-apple-samplecode-landmarks-546`, WebSocket Open, 21 metric samples, 13 FPS samples, 1 screenshot, 28 tools, app icon sync, validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `a8f60dca16a04ae68b4f555d369a3d08`.
- `amitburst__HackerNews` rerun with `--pod-install`, `--exclude-simulator-arm64`, `--relax-warnings`, and `--deployment-target 17.0` after the validator began forcing `arch=x86_64` for arm64-excluded simulator builds: CocoaPods install succeeded and Ansight Studio live session `me-amitburst-hn-548` reported WebSocket Open, 38 metric samples, 23 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync with a 120x120 PNG session icon, validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `d5f699f5204341bea392b99109176bf1`.
- `Finb__Bark` rerun with `--pod-install`, `--exclude-simulator-arm64`, and `--deployment-target 17.0`: CocoaPods install succeeded and Ansight Studio live session `me-fin-bark-547` reported WebSocket Open, 17 metric samples, 12 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync, validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `3c6b2d0a21ac442d80b695d6f28cea9d`.
- `hilen__TSWeChat` rerun with `--pod-install`, `--exclude-simulator-arm64`, `--relax-warnings`, and `--deployment-target 17.0`: CocoaPods install succeeded, Studio pairing/preparation completed for config `5a8a41f21fc545e89ca6f53a5ef85bb4`, and the validator advanced past the older `YYText` `-Wparentheses` failure and the arm64-only `TSVoiceConverter` simulator link failure by using the x86_64 destination. The app still fails before SDK runtime validation because the `TimedSilver` pod source uses removed Swift symbols such as `UIImageOrientation`, which Swift now requires as `UIImage.Orientation`.
- `mattrubin__authenticator` attempted with the Studio validation profile but failed during resolution because the discovered `Authenticator` scheme only advertises macOS destinations (`My Mac`), not an iOS Simulator destination.
- `mssun__passforios` attempted with the Studio validation profile but failed during `xcodebuild -showBuildSettings` while SwiftPM resolved external packages including `SwiftFormat` and `ObjectivePGP`; a 180-second timeout was recorded before SDK runtime validation. The validator runner is now hardened to terminate timed-out Xcode process groups so broad runs do not leave SwiftPM git children behind.
- `tnantoka__edhita` attempted with the Studio validation profile and config `75f98843c8554461833fdcf8dcdea430`; CocoaPods/dependency preparation and Studio pairing succeeded, but the app fails before SDK runtime validation because the project references a missing source file, `Edhita/Models/Constants.swift`.
- `newlinedotco__FlappySwift` rerun with `--pod-install`, `--exclude-simulator-arm64`, `--relax-warnings`, and `--deployment-target 17.0`: Ansight Studio live session `io-fullstack-flappybird-549` reported WebSocket Open, 12 metric samples, 8 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync with a 120x120 PNG session icon, validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `a108c9cddef244d38ce725a2e68b75f9`.
- Follow-up manual touch validation on `newlinedotco__FlappySwift` relaunched the app, confirmed Studio app state returned to foreground, and generated macOS/Simulator clicks against the visible game scene. Studio logs did not record a new touch payload from those clicks, so physical simulator/device gesture validation remains open rather than claimed.
- `newlinedotco__FlappySwift` rerun with `--pod-install`, `--exclude-simulator-arm64`, `--relax-warnings`, `--deployment-target 17.0`, `--inject-validation-touch-input`, and `--studio-require-touch-input`: Ansight Studio live session `io-fullstack-flappybird-551` reported WebSocket Open, 35 metric samples, 23 FPS samples, 2 screenshots, a 28-tool catalog, app icon sync with a 120x120 PNG session icon, validation route, device profile details, `touchCount=2`, `gestureCount=1`, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `05a653476737421c93e361bc6a742a44`.
- `newlinedotco__FlappySwift` rerun with `--inject-validation-input-overlay` and `--studio-require-input-overlay` in addition to the touch/icon/route/device/binary gates: Ansight Studio live session `io-fullstack-flappybird-553` reported WebSocket Open, 42 metric samples, 24 FPS samples, 2 screenshots, a 28-tool catalog, app icon sync, validation route, device profile details, `touchCount=2`, `gestureCount=1`, and a screenshot-frame marker count of `47296` magenta input-overlay pixels; `files.begin_binary_download` metadata also passed for transfer id `d27cd5b92b614d8ca4507c50d1b6d8d4`.
- `Clean-Swift__CleanStore` rerun on the explicit iPhone 17e iOS 26.4 simulator with app-icon, route, device-profile, binary-download, synthetic touch, and picker-style input-overlay gates: Ansight Studio live session `com-clean-swift-cleanstore-557` reported WebSocket Open, 64 metric samples, 37 FPS samples, 4 screenshots, a 28-tool catalog, app icon sync, validation route, device profile details, `touchCount=2`, and `124110` magenta input-overlay pixels. The immediately preceding booted-iPad run `com-clean-swift-cleanstore-556` made the validation field first responder but produced zero marker pixels, so modal picker overlay validation should target an iPhone simulator until the iPad software-input surface behavior is separately characterized.
- After removing generated-template dependencies on Swift-version-specific UIKit names, strict iPhone 17e validation also passed for `austinzheng__swift-2048`, `ivanvorobei__SwiftUI__Other-Projects__Animating-Views-And-Transitions__Complete__Landmarks`, `Finb__Bark`, `amitburst__HackerNews`, and `jordibruin__Swift-Charts-Examples`; sessions `f3nghuang-swift-2048-559`, `com-example-apple-samplecode-landmarks-560`, `me-fin-bark-561`, `me-amitburst-hn-562`, and `com-goodsnooze-swift-charts-examples-563` each reported WebSocket Open, FPS samples, screenshots, the 28-tool catalog, app icon sync, validation route, device profile details, `touchCount=2`, input-overlay marker pixels, and binary-download metadata.
- A second strict iPhone 17e batch also verified `unixzii__SwiftUI-2048` plus five additional Apple tutorial samples: `Building-Lists-And-Navigation__Complete__Landmarks`, `Handling-User-Input__Complete__Landmarks`, `Working-With-UIControls__Complete__Landmarks`, `Creating-And-Combining-Views__Complete__Landmarks`, and `Composing-Complex-Interfaces__Complete__Landmarks`; sessions `com-cyandev-swiftui2048-564`, `com-example-apple-samplecode-landmarks-565`, `com-example-apple-samplecode-landmarks-566`, `com-example-apple-samplecode-landmarks-567`, `com-example-apple-samplecode-landmarks-568`, and `com-example-apple-samplecode-landmarks-569` each passed the same strict Studio gate set.
- A third strict iPhone 17e batch verified `Drawing-Paths-And-Shapes__Complete__Landmarks` and `Interfacing-With-UIKit__Complete__Landmarks` in sessions `com-example-apple-samplecode-landmarks-570` and `com-example-apple-samplecode-landmarks-571`. Seven starting-point tutorial variants failed before SDK runtime validation on stale sample-source APIs such as `Length`, `BindableObject`, `NavigationButton`, and `.identified`.
- A fourth strict iPhone 17e batch verified six smaller SwiftUI samples: `Calculator`, `Basic-Animation`, `Card-Animation`, `Time-Travel`, `Currency-SwiftUI`, and `GitHub-Search`; sessions `hotchner-tk-calculator-572`, `io-designcode-swiftui-basicanimation-573`, `io-designcode-swiftui-cardanimation-574`, `com-tdonnelly-swiftuitimetravel-575`, `alex-liu-currency-swiftui-576`, and `jp-marty-suzuki-githubsearchwithswiftui-577` each passed the same strict Studio gate set.
- After qualifying the generated validation bootstrap's concurrency task as `_Concurrency.Task`, `Example-To-Do-App` no longer collides with the app's own `Task` model type and passed strict Studio validation in session `kr-xoul-swiftuitodo-583`. The same batch verified `Combine-using-GitHub-API`, `Flux`, `Jike`, `MovieSwift`, and `SwiftUI-Redux` in sessions `com-ryo-swiftui-combine-example-578`, `com-ryo-swiftui-flux-579`, `com-shigy-swiftui-jike-580`, `com-thomasricouard-movieswift-581`, and `com-thomasricouard-swiftuidemo-582`; `2048-Game` and `InstaFake` failed before SDK runtime validation on sample-source build errors.
- A sixth strict iPhone 17e batch verified `Tempus-RomanumIl`, `UINote`, `WWDCPlayer`, and `iPadOS-Scenes` in sessions `org-poikile-tempusromanum-584`, `io-github-agiletalk-swiftuinote-585`, `com-daybreak-wwdcplayer-586`, and `com-twolivesleft-behindthescenes-587`; each passed the same strict Studio gate set. `fullmoon-ios` still fails before SDK runtime validation during SwiftPM package resolution, and `mlx-swift-examples` now gets past macro validation when the validator passes `-skipMacroValidation`/`-skipPackagePluginValidation` but then fails before SDK runtime validation because this Xcode installation is missing the Metal Toolchain (`xcodebuild -downloadComponent MetalToolchain`).
- The validator now writes `.ansight-validation/ios-test-app-validation-summary.json`; the current merged strict-gate summary records `totalApps=42`, `studioVerifiedCount=31`, `sdkRuntimeReachedCount=31`, `preRuntimeFailureCount=11`, no Studio verification failures, and passing FPS, screenshot, remote-tool, icon, device-profile, route, touch-input, input-overlay, and binary-download metadata gates for all 31 runtime-reaching rows. The remaining pre-runtime failures are classified as `stale_swift_api=5`, `build_failure=4`, `resolution=1`, and `missing_host_toolchain=1`.
- Resume/merge smoke validation passed: running the validator with `--merge-results .ansight-validation/ios-test-app-validation-results.json --skip-verified-results --app newlinedotco__FlappySwift` skipped the existing Studio-verified FlappySwift row, wrote merged results, and preserved the passing summary without requiring another simulator launch.
- `jordansinger__SwiftUI-Kit` initially selected a watchOS scheme; after the validator's iOS target/scheme preference fix it resolved the intended `SwiftUI Kit iOS` target and issued config `aa271cd534f14ce499cf6ca83b963030` for `com.swiftui.kit`. Build still fails before SDK runtime validation because that iOS scheme embeds a watch app and this Xcode installation does not have watchOS 26.4 installed; `--skip-unavailable-actions` did not bypass the embedded watchOS requirement.
- `jordibruin__Swift-Charts-Examples` rerun with `--pod-install`, `--exclude-simulator-arm64`, `--relax-warnings`, and `--deployment-target 17.0`: Ansight Studio live session `com-goodsnooze-swift-charts-examples-550` reported WebSocket Open, 48 metric samples, 31 FPS samples, 1 screenshot, a 28-tool catalog, app icon sync with a 120x120 PNG session icon, validation route, device profile details, and `files.begin_binary_download` metadata for a 150000-byte validation payload with transfer id `c9820ef7e6b04debbd199c010ce7f623`.
- CocoaPods validation: `pod lib lint Ansight.podspec --private --allow-warnings --skip-tests --platforms=ios '--include-podspecs=*.podspec' --use-static-frameworks` passed for the aggregate pod, all local SDK/tool pod dependencies, and the `AnsightCore` developer build-artifact script phase.
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
- Common pre-runtime failures observed in the corpus include missing CocoaPods xcconfig files before `--pod-install` remediation (`AudioKit__AudioKitSynthOne`, `JakeLin__SwiftLanguageWeather`), missing app-specific source/config files (`tnantoka__edhita`), stale third-party Swift dependencies (`hilen__TSWeChat` via `TimedSilver` `UIImageOrientation`), missing local platform runtimes for embedded watch apps (`jordansinger__SwiftUI-Kit`), macOS-only schemes in the iOS corpus (`mattrubin__authenticator`), unresolved module dependency `Popovers` (`aheze__OpenFind`), stale SwiftUI APIs (`devxoul__SwiftUITodo`), unresolved bundle id (`coderyi__Monkey`), and `xcodebuild -showBuildSettings` timeouts (`analogcode__Swift-Radio-Pro`, `glushchenko__fsnotes`, `horizontalsystems__unstoppable-wallet-ios`, `mssun__passforios`).
- `unixzii__SwiftUI-2048` installed and launched through `scripts/validate_ios_test_apps.py` on iPhone 17e iOS 26.4 with config `99de3dd6f3824fb6ac5b6771de51c2a9`.
- Ansight Studio live session `com-cyandev-swiftui2048-522` reported WebSocket Open, foreground lifecycle state, automatic `GameView` screen-view capture from `UIHostingController<ModifiedContent<GameView, ...>>`, FPS samples, and JPEG screenshot capture.
- `devxoul__SwiftUITodo` currently fails before SDK validation on the current Xcode/Swift toolchain because the app source still uses removed SwiftUI APIs such as `@propertyDelegate` and `BindableObject`.
