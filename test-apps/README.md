# SDK test apps

SDK validation apps are grouped by feature suite. Each platform app keeps its
build project and, when applicable, its Ansight test workspace alongside its
source.

| Suite | Apps | Device requirement | Purpose |
| --- | --- | --- | --- |
| [`core`](core/README.md) | Android, iOS, Capacitor, Flutter, and .NET | Simulator or emulator for mobile tests; Mac host for desktop tests | Broad SDK and integration coverage |
| [`motion`](motion/README.md) | Android and iOS | Android emulator or iOS simulator for automated injection | Opt-in motion capture and app-owned sampling |

Independent host and device test apps live in the separate
[`ansight-test-apps`](../../../ansight-test-apps/README.md) checkout.
