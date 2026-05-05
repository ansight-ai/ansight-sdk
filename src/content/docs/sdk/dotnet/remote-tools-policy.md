---
title: .NET Remote Tools Policy
description: Configure Ansight build-time handling for remote tool packages and custom ITool implementations.
---

Ansight can scan .NET build outputs for concrete `Ansight.Tools.ITool` implementations. This covers bundled tool packages and custom in-app remote tools copied to `$(TargetDir)`.

Use `AnsightRemoteToolsPolicy` to choose the build behavior:

- `Allowed`: bypasses remote tool scanning, warnings, and detected-tool logging.
- `AllowedWithWarnings`: scans for remote tools, logs detected tool type and assembly details, emits a build warning when tools are present, and allows the build to continue. This is the default.
- `Disallowed`: scans for remote tools, logs detected tool type and assembly details, and fails the build when tools are present.

When the resolved policy is `Allowed` or `AllowedWithWarnings`, Ansight sets `AnsightRemoteToolsEnabled=true` and adds the `ANSIGHT_REMOTE_TOOLS` compile-time symbol. `Disallowed` sets `AnsightRemoteToolsEnabled=false` and omits that symbol.

For strict Release or CI builds:

```xml
<PropertyGroup>
  <AnsightRemoteToolsPolicy>Disallowed</AnsightRemoteToolsPolicy>
</PropertyGroup>
```

`Disallowed` will not work with the `Ansight` or `Ansight.Maui` all-in-one packages as-is because those packages intentionally include remote tools. To exercise this policy, use `Ansight.Core` plus the fine-grained `Ansight.Tools.*` packages and condition the tool references out of protected Release or CI builds.

Detected tool logging is enabled by default. To suppress the type and assembly list while keeping the selected policy behavior:

```xml
<PropertyGroup>
  <AnsightLogRemoteTools>false</AnsightLogRemoteTools>
</PropertyGroup>
```

Use `Allowed` only when the build intentionally includes remote tools and you do not want build-time checks or warnings. Remote tools can expose screenshots, UI state, filesystem data, database contents, preferences, secure storage, and live runtime state to a connected host.
