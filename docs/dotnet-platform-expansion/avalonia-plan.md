# Avalonia Plan

## Goal

Deliver full Ansight parity for Avalonia applications while respecting that Avalonia owns its own windowing, visual tree, rendering, and accessibility model.

## Target Outcome

- Avalonia apps get a dedicated integration package such as `Ansight.Avalonia`
- lifecycle, FPS, screenshots, and visual tree tools operate on native Avalonia concepts
- the existing Ansight tool IDs and runtime APIs remain intact
- desktop storage support is explicit rather than implied

## Scope

- primary target: Avalonia desktop applications
- Windows-only rollout is acceptable as the first milestone if it is treated as phase one, not as full Avalonia completion
- later phases may extend to macOS and Linux desktop heads

## Implementation Plan

### 1. Add a dedicated Avalonia integration package

Create a package such as `Ansight.Avalonia`.

Responsibilities:

- register Avalonia lifecycle bridge
- register Avalonia frame monitor
- register Avalonia session capture backend
- register Avalonia visual tree backend
- register platform-appropriate storage providers

### 2. Lifecycle integration

Map Avalonia application lifetime and window activity into the shared Ansight lifecycle model.

Required behaviors:

- foreground when the app has an active visible window
- background when all windows are hidden, minimized, or inactive according to the shared desktop lifecycle policy
- clean shutdown when the application lifetime ends

The integration should support the lifetime model used by the team's Avalonia apps.

### 3. FPS telemetry

Implement an Avalonia render loop FPS source.

Requirements:

- measure real render cadence, not timer cadence
- reset cleanly when windows are closed or recreated
- work with the current `IFrameRateMonitor` contract

### 4. Memory telemetry and device profile

Reuse the generic desktop process memory implementation where possible.

Runtime profile requirements:

- identify `dotnet`
- identify host OS
- identify `avalonia`

When later phases add macOS or Linux heads, the device profile enricher should stay consistent with the same runtime stack contract.

### 5. Session JPEG capture

Implement an Avalonia session capture backend.

Requirements:

- capture the active root visual or top-level content
- support resizing and JPEG quality controls
- operate correctly with high DPI scaling
- define behavior for hidden or off-screen windows

### 6. Visual tree and screenshot tools

Implement an Avalonia visual tree backend behind the existing tool IDs.

Requirements:

- traverse the Avalonia visual tree
- expose bounds, visibility, enabled state, focusability, and automation metadata
- support multiple windows, popups, overlays, and dialogs
- support screenshot annotation by node ID
- keep IDs stable during the capture session

The backend should use Avalonia-native concepts rather than forcing WinUI or WPF assumptions into the node model.

### 7. Preferences and secure storage

Avalonia does not provide one canonical preferences or secure storage abstraction that maps cleanly to every app.

Therefore desktop provider registration is required.

Recommended first-party providers:

- JSON or settings-file preferences provider stored under app data
- Windows secure storage provider for Windows desktop phase one

Future providers by host OS may include:

- macOS Keychain
- Linux Secret Service or equivalent

The package should not claim that it can discover or mutate arbitrary app settings unless the app has explicitly registered the provider that owns those settings.

### 8. Harness and verification

Add an Avalonia harness with:

- multiple windows
- popups or overlays
- an animation surface for FPS validation
- seeded preferences and secure storage values through registered providers

Validation matrix:

- lifecycle
- FPS
- memory channels
- visual tree capture
- screenshot capture
- preferences and secure storage round-trips
- file and database tools
- host pairing behavior

## Milestones

1. `Ansight.Avalonia` package skeleton
2. Lifecycle, memory, and FPS
3. Avalonia screenshot and visual tree backend
4. Provider-based storage support
5. Windows desktop harness and CI
6. macOS and Linux expansion if required

## Main Risks

- no canonical built-in Avalonia storage model
- render-loop integration differences across desktop hosts
- popup and overlay capture behavior
- allowing the package to stay honest about what it can inspect versus what the host app must register explicitly
