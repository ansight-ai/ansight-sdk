# Screenshot safe areas and system-UI metadata

Status: implemented in source, 18 October 2026. SDK and host rebuilds are required; this document does not announce a published release. The original proposal used `captureGeometry`; the public option is **`includeSystemUiMetadata`** (`IncludeSystemUiMetadata` in .NET).

## The 101

A screenshot gives us pixels. Its optional `geometry` describes the layout **when those pixels were captured**. Safe-area insets describe usable layout, not proof that a clock appears in the image. System-UI entries say whether a status bar, navigation bar or display cutout is present, absent or unknown.

Capture metadata defaults on. Tests separately opt into ignoring a region. No OCR, private APIs, guessed device-size tables or new screen-recording permissions are used.

```ts
const actual = await ansight.screenshots.capture({ format: "png" });
const comparison = await ansight.screenshots.compare({
  image: { frameId: actual.frameId },
  reference: { workspacePath: "references/home.png" },
  ignoreSystemUi: ["statusBar", "navigationBar"],
});
expect(comparison.passed, { id: "home-matches" }).toBe(true);

// Only this capture opts out; the screenshot itself is still retained.
const pixelsOnly = await ansight.screenshots.capture({ includeSystemUiMetadata: false });
```

## SDK configuration

The setting belongs to existing screenshot options, alongside quality, size and cadence. It is independent for periodic capture, annotation capture and individual screenshot requests. Disabling periodic metadata does not override an explicit on-demand capture request. Offline capture preserves its configured screenshot option.

```csharp
// .NET / MAUI
options.WithSessionJpegCapture(includeSystemUiMetadata: false);
// Or: new SessionJpegCaptureOptions { IncludeSystemUiMetadata = false }
```

```kotlin
// Native Android
builder.withSessionJpegCapture(includeSystemUiMetadata = false)
```

```swift
// Native Apple SDK
builder.withSessionJpegCapture(includeSystemUiMetadata: false)
```

```dart
// Flutter
const AnsightSessionJpegCaptureOptions(includeSystemUiMetadata: false)
```

```ts
// React Native and Capacitor
builder.withSessionJpegCapture({ includeSystemUiMetadata: false });
```

Annotation options also expose `includeSystemUiMetadata` / `IncludeSystemUiMetadata`, default true. The .NET annotation builder accepts it in `WithScreenshotEncoding`. Existing `ui.get_screenshot` requests accept the boolean directly. False skips geometry collection and metadata delivery; pixels are unchanged. Unsupported MAUI platforms retain their no-op integration.

## Shared contract

`geometry` is optional on capture results, frame records and inspection images:

```ts
interface ScreenshotCaptureGeometry {
  coordinateSpace: "image-normalized";
  captureTarget: "display" | "appWindow" | "appContent";
  safeAreaInsets: { top: number; right: number; bottom: number; left: number } | null;
  systemUi: Record<"statusBar" | "navigationBar" | "displayCutout",
    | { state: "present"; regions: Rect[]; reason?: string }
    | { state: "absent"; regions: []; reason?: string }
    | { state: "unknown"; regions: []; reason: string }>;
}
interface Rect { x: number; y: number; width: number; height: number; }
```

Coordinates use the final upright image, from 0 to 1. Resizing the whole image preserves them. Insets must leave a nonempty safe area. Present regions must be finite positive rectangles within the image. Missing metadata is unknown, never an empty mask.

## Provider behavior

| Provider | Behavior |
| --- | --- |
| Kotlin Android and .NET Android | App-window capture; public WindowInsets supply safe-area and cutout bounds. System bar glyphs belong to OS compositor layers, so status/navigation bars are absent from these images. Keyboard/IME insets are excluded. |
| Swift Apple and .NET iOS/Mac Catalyst | App-window capture; safeAreaInsets supply layout. OS status/home-indicator layers are absent. Exact cutout bounds are unknown; safe-area height is not substituted. |
| React Native and Capacitor | Native capture uses the Android/Apple behavior; native bridges preserve configuration and metadata. |
| Flutter mobile | Native capture uses the corresponding platform provider. |
| Flutter desktop raster | App-content target; maps view padding to the actual render boundary. Unsupported transforms/layout changes do not produce invented safe areas. |
| External host device capture | Geometry unavailable until that provider supplies image-bound metadata. It does not borrow SDK geometry. |

An updated SDK requesting periodic metadata retains SDK capture ownership rather than being replaced by an external provider that cannot supply it. Render fallbacks or detected layout transitions omit unreliable metadata. A window inset alone does not identify a dynamic app clock, keyboard or arbitrary overlay; use explicit app masks for those.

## Retention and transport

On-demand tool results, action screenshots, periodic frames, annotation bundles and offline indexes carry the same optional field. Host frame indexes, archives, exports and replay preserve it.

The ASJP JPEG binary header and bytes remain unchanged. New hosts advertise `screenshotGeometry: true`. Only then do SDK streams send a `CLIENT_SCREENSHOT_GEOMETRY` companion message, paired by encoded timestamp, dimensions and SHA-256 of the exact JPEG. The host join is connection-local, limited to 128 entries with a 30-second expiry, and handles either arrival order. Conflicting or ambiguous pairs lose metadata. Late updates and invalidations are journaled with the frame.

There is no schema version fork or migration. Old recordings remain usable, with unknown geometry. Old hosts receive the existing binary stream. Recompile apps for the updated SDK; source defaults are preserved, but changing platform constructors is not a promise of binary ABI compatibility. Metadata collection and JPEG hashing add work to enabled periodic capture; no performance benchmark is claimed here.

## Explicit comparison masks

`compare` and `assert` accept `ignoreSystemUi: ["statusBar", "navigationBar", "displayCutout"]`. Only requested entries on the **actual** frame are resolved. A present entry contributes rectangles; absent contributes none; missing/unknown geometry returns `screenshot_geometry_unavailable` with recovery guidance. `assert` rejects `includeSystemUiMetadata: false` combined with requested system masks before taking a screenshot.

Automatic and manual `ignoreRegions` form one union. Overlaps count once; at most 64 combined rectangles are allowed; excluding every pixel is an error. Excluded pixels do not affect scoring, difference highlights or extracted difference regions. Original images remain intact. Results retain `systemUiMaskResolutions` with the source frame ID, and replay labels absent/excluded system UI.

## Validation harness

The independent `ansight-test-apps/screenshot-geometry` MAUI app provides a deterministic app clock and colored blocks. Its task checks capture opt-out, retained insets, absent OS bars, explicit clock masks and a block change outside the mask. Host fixtures cover present bars, unknown/legacy geometry, overlap counting, stream matching, bounded eviction and late persistence. Platform-wide combinations such as split-screen, rotation during capture and physical-device cutouts still require device coverage; metadata must remain conservative when unavailable.
