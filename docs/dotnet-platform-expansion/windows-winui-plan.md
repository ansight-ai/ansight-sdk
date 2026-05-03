# Windows / WinUI 3 Plan

## Goal

Deliver full Ansight parity for .NET MAUI apps running on Windows through the native WinUI 3 backend used by MAUI.

This plan targets the existing feature suite, not reduced desktop support.

## Target Outcome

- `Ansight` and the tool packages add a Windows target framework
- the MAUI test harness adds a Windows head
- all existing features operate on WinUI 3 primitives on Windows
- MAUI Windows apps use the same public Ansight APIs they use on Android and Apple today

## Scope

- primary target: .NET MAUI Windows via WinUI 3
- out of scope for this document: WPF
- reuse target: Uno WinAppSDK heads may reuse most of this backend

## Implementation Plan

### 1. Add Windows target frameworks

Update:

- `src/dotnet/Ansight.Core/Ansight.Core.csproj`
- `src/dotnet/Ansight.Tools.VisualTree/Ansight.Tools.VisualTree.csproj`
- `src/dotnet/Ansight.Tools.Preferences/Ansight.Tools.Preferences.csproj`
- `src/dotnet/Ansight.Tools.SecureStorage/Ansight.Tools.SecureStorage.csproj`
- `src/dotnet/Ansight.Tools.Database/Ansight.Tools.Database.csproj`
- `src/dotnet/Ansight.Tools.FileSystem/Ansight.Tools.FileSystem.csproj`
- `src/dotnet/Ansight.Tools.Reflection/Ansight.Tools.Reflection.csproj`

Recommended target:

- `net9.0-windows10.0.19041.0`

### 2. Add a WinUI platform package or platform service registration layer

Create a Windows-specific integration package that registers:

- WinUI lifecycle bridge
- WinUI frame rate monitor
- Windows memory sampler
- WinUI session capture backend
- WinUI visual tree backend
- Windows device profile enricher
- Windows preferences backend
- Windows secure storage backend

This package should be referenced automatically by MAUI Windows hosts in the test harness first, then documented for app consumption.

### 3. Lifecycle integration

Map WinUI application and window events into the existing app lifecycle stream.

Required behaviors:

- first shown or activated window maps to foreground
- all windows hidden, minimized, or deactivated for a sustained period maps to background
- app shutdown emits the final state cleanly
- multi-window MAUI apps use one process-wide lifecycle state

### 4. FPS telemetry

Implement a WinUI frame monitor using the WinUI render loop.

Requirements:

- emit samples at the same cadence used by the current mobile backends
- do not report FPS when no active window content is rendering
- reset cleanly when windows close or are recreated

### 5. Memory telemetry

Implement a Windows memory sampler that fills the existing channels as faithfully as possible.

Recommended mapping:

- `ManagedHeap`: `GC.GetTotalMemory(false)`
- `ResidentSetSize`: process working set
- `NativeHeap` or `PhysicalFootprint`: process private bytes or private usage, depending on which is more stable and more comparable for tooling

This mapping must be documented so cross-platform charts remain interpretable.

### 6. Device and runtime profile collection

Extend `DeviceAppProfileCollector` to report:

- OS name as `windows`
- OS version
- device class as desktop
- process architecture
- app identity and package identity when available
- runtime stack entries for `dotnet`, `windows`, and `winui`
- MAUI framework overlay metadata when it can be collected honestly

### 7. Session JPEG capture

Implement `SessionJpegCaptureSupport.Windows.cs`.

Requirements:

- capture the current foreground window root
- support width scaling and JPEG quality options
- preserve the existing streaming contract over pairing transport
- handle windows without content and windows not yet ready for capture
- behave correctly on the UI thread

### 8. Visual tree and screenshot tools

Implement a WinUI visual tree backend behind the existing tool IDs.

Requirements:

- traverse from the active root content element
- use WinUI visual tree traversal
- expose bounds, visibility, enabled state, focusability, and automation label data
- support `get_visual_tree`, `inspect_node`, and screenshot capture
- support screenshot annotation by node ID
- define behavior for popups, flyouts, dialogs, and XAML islands

The backend should inspect native WinUI tree state, not invent a MAUI-only tree model.

### 9. Preferences and secure storage

Implement Windows backends with real persistence.

Preferences:

- default backend should use Windows app data settings when available
- support named stores through a stable namespace convention

Secure storage:

- choose one real Windows-backed implementation and use it consistently
- packaged and unpackaged MAUI desktop builds must both be supported
- storage identity must remain stable across app restarts and debug sessions

This is the major design gate in the Windows plan. If packaged-only APIs are insufficient, use a direct Windows data-protection approach rather than pretending generic secure storage exists.

### 10. Harness and verification

Add Windows to `src/dotnet/tests/Ansight.TestHarness/Ansight.TestHarness.csproj`.

Validation matrix:

- startup and lifecycle transitions
- FPS with an actively animating page
- memory channel sampling
- session JPEG streaming
- `ui.get_visual_tree`
- `ui.inspect_node`
- `ui.get_screenshot`
- preferences and secure storage round-trips
- file and database tools
- auto-probe reconnect behavior

## Milestones

1. Shared platform service refactor
2. Windows lifecycle, memory, FPS, and device profile
3. Windows screenshot and visual tree backend
4. Windows storage backends
5. Windows MAUI harness and CI hardening

## Main Risks

- unpackaged versus packaged Windows storage behavior
- multi-window MAUI semantics
- screenshot capture for popups, composition surfaces, and XAML islands
- deciding whether MAUI metadata belongs in the runtime profile or in an overlay field
