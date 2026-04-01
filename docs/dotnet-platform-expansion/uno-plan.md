# Uno Plan

## Goal

Deliver full Ansight parity for Uno applications without collapsing distinct Uno heads into a fake single implementation.

## Core Principle

Uno must be planned by head, because "Uno support" is really a family of host implementations:

- WinAppSDK head
- Skia desktop heads
- other heads that may exist later

The public Ansight API and tool contracts can stay shared, but UI capture, lifecycle, FPS, and storage behavior must be implemented per head where required.

## Target Outcome

- Uno apps get a dedicated integration package such as `Ansight.Uno`
- WinAppSDK-based Uno apps reuse the WinUI backend wherever possible
- Skia-based Uno desktop heads get a dedicated Uno implementation for lifecycle, rendering, screenshots, and visual tree capture
- storage support is explicit and honest per head

## Implementation Plan

### 1. Define supported Uno heads

Before coding, lock the support matrix.

Recommended first wave:

- Uno WinAppSDK head on Windows
- Uno Skia desktop heads used by the team

Do not claim parity for every Uno head until each one has been validated independently.

### 2. Add a dedicated Uno integration package

Create a package such as `Ansight.Uno`.

Responsibilities:

- register lifecycle and render hooks for the active Uno head
- expose a simple setup method from the Uno app bootstrap path
- register the correct visual tree and screenshot backend for the head
- register the correct storage providers for the head

### 3. WinAppSDK head rollout

For Uno WinAppSDK:

- reuse the Windows memory sampler
- reuse the Windows device profile enricher
- reuse the WinUI session capture backend where Uno is truly running on WinUI 3
- reuse the WinUI visual tree backend if the Uno head exposes a real WinUI tree with the required properties

Even in the reuse path, validate:

- Uno control metadata
- popups and overlays
- screenshot capture correctness
- node ID stability

### 4. Skia desktop head rollout

For Uno Skia heads:

- implement a Uno-specific lifecycle bridge
- implement a Uno-specific render loop FPS source
- implement a Uno-specific screenshot backend
- implement a Uno-specific visual tree backend

The backend must operate on actual Uno UI state and not pretend that WinUI APIs are available everywhere with identical semantics.

### 5. Telemetry parity

For each supported head, deliver:

- app lifecycle events
- memory metrics
- FPS metrics
- device and runtime profile enrichment
- session JPEG capture

Runtime profile requirements:

- identify `dotnet`
- identify `windows` where applicable
- identify `uno`
- identify the active head when it materially affects behavior

### 6. Visual tree parity

Uno visual tree support must define one stable backend contract for:

- root discovery
- child traversal
- bounds
- visibility
- enabled state
- focusability
- automation label metadata
- popup and overlay handling

If Uno head behavior diverges, normalize the output in the Uno adapter rather than in the shared tool schema.

### 7. Preferences and secure storage

Storage support must be head-aware.

Recommended approach:

- WinAppSDK head reuses the Windows-backed preferences and secure storage approach
- Skia heads use explicit provider registration

Do not promise generic Uno secure storage discovery unless the target head exposes a real platform-owned store with stable semantics.

### 8. Harness and verification

Add at least one Uno harness per supported head.

Each harness must verify:

- startup and lifecycle
- FPS under animation
- screenshot capture
- visual tree inspection
- file and database tools
- preferences and secure storage through the configured backend
- reconnect and teardown behavior

## Milestones

1. Lock supported Uno heads
2. Ship WinAppSDK head support by reusing the WinUI path where valid
3. Ship Uno Skia head support for the desktop heads the team cares about
4. Add head-specific harnesses and CI
5. Expand to additional Uno heads only after parity is proven

## Main Risks

- treating Uno as one runtime when it is really many heads
- assuming WinUI APIs imply identical behavior on Skia heads
- storage parity across different heads
- test explosion if supported heads are not locked early
