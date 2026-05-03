# WPF Plan

## Goal

Deliver full Ansight parity for WPF applications on Windows while preserving the existing public feature suite and tool contracts.

## Target Outcome

- WPF apps can initialize Ansight without MAUI
- all existing telemetry and tool surfaces remain available
- WPF-specific integration lives in a dedicated package rather than being hidden behind MAUI assumptions

## Scope

- primary target: modern WPF on .NET 9 Windows desktop
- shared Windows services should be reused from the WinUI rollout when possible
- WPF must own its own lifecycle, visual tree, screenshot, and storage integration

## Implementation Plan

### 1. Add a dedicated WPF integration package

Create a package such as `Ansight.Wpf`.

Responsibilities:

- register WPF lifecycle bridge
- register WPF visual tree backend
- register WPF screenshot backend
- register WPF frame rate monitor
- register Windows memory and device profile services
- expose a simple integration call from `App.xaml.cs`

`Ansight.Core` should not try to infer WPF state without explicit integration.

### 2. Lifecycle integration

Map WPF application and window activity into the existing app lifecycle state stream.

Required behaviors:

- foreground when at least one interactive window is shown and active
- background when all windows are hidden, minimized, or the app is deactivated
- exit cleanly tears down runtime-owned services
- owned windows, modal dialogs, and tray-only behavior are handled explicitly

### 3. FPS telemetry

Implement a WPF frame monitor on the WPF composition/render loop.

Requirements:

- track only while at least one active render surface exists
- reset cleanly on dispatcher shutdown or window recreation
- preserve the same read model used by current `IFrameRateMonitor`

### 4. Memory telemetry and device profile

Reuse the Windows process memory implementation introduced for WinUI where possible.

Extend runtime profile reporting to include:

- `windows`
- `wpf`
- `dotnet`

This should distinguish WPF from WinUI in the runtime stack so the host can reason about framework-specific UI capabilities.

### 5. Session JPEG capture

Add a WPF session capture backend.

Requirements:

- capture from the active root visual
- support scaling and JPEG quality settings
- behave correctly with layered windows, transforms, and high DPI
- define fallback behavior for hidden or zero-size windows

### 6. Visual tree and screenshot tools

Add a WPF visual tree backend behind the current tool IDs.

Requirements:

- walk the WPF visual tree
- capture native WPF element state, not an invented abstraction
- expose bounds, visibility, enabled state, focusability, and automation metadata
- support popups, adorners, menus, and modal dialog windows
- keep node IDs stable within a capture session

### 7. Preferences and secure storage

WPF does not own a single canonical app preferences or secure storage surface.

To preserve the existing tool suite honestly, add provider-based desktop storage integration:

- preferences tools accept a WPF storage provider registration
- secure storage tools accept a WPF secure storage provider registration

Ship first-party adapters for:

- `ApplicationSettingsBase` or other .NET settings-based preferences providers
- DPAPI-backed secure storage

WPF apps must opt into the adapter that matches the storage layer they actually use. Do not claim generic WPF storage discovery without a real backing provider.

### 8. Harness and verification

Add a WPF test harness project with:

- a multi-window sample
- modal dialogs
- popups and menus
- animated content for FPS validation
- seeded preferences and secure storage values

Validation matrix:

- lifecycle transitions
- FPS emission under animation
- visual tree inspection across normal windows and dialogs
- screenshot capture correctness at high DPI
- storage tool round-trips through registered providers
- pairing reconnect and cleanup

## Milestones

1. Shared Windows services reused from the WinUI plan
2. `Ansight.Wpf` package with lifecycle and FPS
3. WPF screenshot and visual tree backend
4. WPF provider-based storage support
5. WPF harness and CI

## Main Risks

- no single canonical WPF storage stack
- popup and adorner capture correctness
- multi-window lifecycle policy
- maintaining parity while avoiding WPF-specific leakage into the shared tool schema
