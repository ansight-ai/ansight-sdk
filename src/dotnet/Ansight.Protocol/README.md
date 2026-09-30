# Ansight.Protocol

Portable app-to-host contracts shared by the Ansight SDK and CLI/harness:
device and app profiles, enrollment/control messages, lifecycle values, tool
schemas, envelopes and payload encoding. Namespaces and wire formats are
preserved when public types move from `Ansight.Core`.

The package targets plain .NET 9. It has no SDK runtime, native bindings, mobile
workloads, build tasks, cloud authentication or account requirement. SDK Core
references it and forwards moved public types for existing consumers. The CLI
host consumes this package without the app-side SDK runtime.

Source is available under PolyForm Shield 1.0.0; see the repository's LICENSE.
Third-party components retain their own licences.
