# .NET Platform Expansion Plans

## Summary

This document set defines the work required to bring the Ansight .NET SDK to full feature parity on four additional platform families:

- .NET MAUI on Windows via WinUI 3
- WPF
- Uno Platform
- Avalonia

"Full feature parity" means preserving the existing SDK surface and behavior across:

- runtime initialization and activation
- host pairing and auto-probe
- app lifecycle streaming
- memory telemetry
- FPS telemetry
- device and runtime profile collection
- session JPEG capture
- visual tree and screenshot tools
- reflection tools
- file system tools
- database tools
- preferences tools
- secure storage tools

## Current Constraints In The Repo

The current .NET implementation is still mobile-first:

- `Ansight` targets only `net9.0`, `net9.0-android`, `net9.0-ios`, and `net9.0-maccatalyst`
- `PlatformBootstrapper` only registers mobile FPS services
- `MemorySampler` has real implementations only for Android and Apple
- `SessionJpegCaptureSupport` falls back to unavailable on unsupported hosts
- `VisualTreeSupport` only contains Android and UIKit/Mac Catalyst implementations
- preferences and secure storage backends are only implemented for Android and Apple

Relevant files:

- `src/dotnet/Ansight.Core/Ansight.Core.csproj`
- `src/dotnet/Ansight.Core/Platforms/PlatformBootstrapper.cs`
- `src/dotnet/Ansight.Core/Telemetry/Memory/MemorySampler.cs`
- `src/dotnet/Ansight.Core/Screenshot/SessionJpegCaptureSupport.*`
- `src/dotnet/Ansight.Tools.VisualTree/VisualTreeSupport.cs`
- `src/dotnet/Ansight.Tools.Preferences/PreferencesSupport.*`
- `src/dotnet/Ansight.Tools.SecureStorage/SecureStorageSupport.*`

## Shared Prerequisite Work

All four platform plans depend on the same refactor. Do this once before implementing any single platform deeply.

### 1. Replace ad hoc `#if` seams with platform service registration

Introduce a platform service bundle instead of having each subsystem hard-code its own preprocessor matrix.

Recommended service boundaries:

- `IAppLifecycleBridge`
- `IMemorySampler`
- `IFrameRateMonitor`
- `IDeviceProfileEnricher`
- `ISessionCaptureBackend`
- `IVisualTreeBackend`
- `IPreferencesBackend`
- `ISecureStorageBackend`

`PlatformBootstrapper` should register a concrete implementation set for the active host rather than only FPS.

### 2. Split desktop support from framework support

Desktop Windows support and framework-specific UI support are not the same thing.

- WinUI 3 and WPF can share the Windows memory, process, and storage layers.
- Uno and Avalonia need their own UI tree, screenshot, lifecycle, and storage integration packages.

Keep `Ansight.Core` host-agnostic where possible and add framework packages where required:

- `Ansight.WinUI`
- `Ansight.Wpf`
- `Ansight.Uno`
- `Ansight.Avalonia`

Those package names are recommendations, not fixed requirements.

### 3. Standardize the desktop lifecycle contract

Mobile foreground/background state maps cleanly today. Desktop does not.

Before platform rollout, define one desktop lifecycle policy for:

- initial startup
- first window shown
- window activated
- window deactivated
- all windows hidden or minimized
- application suspended or exiting

The same policy must be used by WinUI, WPF, Uno desktop heads, and Avalonia desktop heads.

### 4. Define the visual tree contract once

The existing tool contract is already shared, but the node model needs a clearer framework-neutral baseline for desktop implementations.

Every backend should be able to provide:

- stable node IDs
- node type
- optional label or automation name
- visibility
- enabled state
- focusability
- bounds in window coordinates
- parent and children
- selected computed properties

Do not promise framework-specific fields unless they are namespaced under an extension payload.

### 5. Decide the desktop storage model

This is the hardest cross-platform parity problem.

- WinUI on Windows has platform-owned app data APIs.
- WPF and Avalonia do not have a single canonical preferences or secure storage surface.
- Uno support varies by head.

To preserve the existing tool set without fake parity:

- keep native default backends where a true platform store exists
- add explicit provider hooks for frameworks that do not own a canonical store
- require apps to opt into the provider that matches their real storage implementation

### 6. Expand test infrastructure

Add new harnesses and CI lanes:

- Windows MAUI harness
- WPF harness
- Uno harness
- Avalonia harness

Each harness must validate:

- runtime startup and shutdown
- pairing and auto-probe
- FPS emission
- memory channels
- screenshot capture
- visual tree capture
- preferences and secure storage round-trips
- file and database tools

## Platform Plans

- [Windows / WinUI 3 Plan](windows-winui-plan.md)
- [WPF Plan](wpf-plan.md)
- [Uno Plan](uno-plan.md)
- [Avalonia Plan](avalonia-plan.md)
