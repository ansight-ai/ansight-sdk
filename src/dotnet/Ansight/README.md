# Ansight

All-in-one Ansight package for .NET apps.

This package references `Ansight.Core`, `Ansight.Annotations`,
`Ansight.OfflineCapture`, native pairing where supported, and all non-MAUI
remote tool packages. The runtime namespace remains `Ansight`.

See the [.NET getting-started guide](https://www.ansight.ai/docs/sdk/dotnet/setup)
for package installation, guarded startup locations, and CLI verification.

```sh
dotnet add package Ansight --prerelease
```

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

```csharp
using Ansight;

#if DEBUG
var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
#endif
```

Start the local host with `ansight host run`. Simulators, Mac Catalyst, and
desktop apps register automatically through loopback; no account or explicit
connection call is required. Verify with
`ansight session list --connected --json` and
`ansight app tools <session-id> --json`.

For a physical device, run `ansight pairing issue --qr`, then call the platform
QR reader from a developer-only app surface:

```csharp
await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.QrCode());
```

The first scan stores this app installation's registration; later launches
reconnect without a pairing file or another scan. The all-in-one package tracks
the current Android activity automatically.

In-app annotations are bundled but deliberately not enabled by the all-in-one defaults. Opt in explicitly from a Debug application build:

```csharp
using Ansight.Annotations;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight => ansight.WithAnnotatedFeedback())
    .Build();

Runtime.InitializeAndActivate(options);
await Feedback.PresentAsync();
```

`WithAnnotatedFeedback()` remains disabled in Release builds. It captures the screenshot and all registered visual-tree sources, supports custom data/artifact hooks, submits to a connected host session, and participates in an active offline capture. See the `Ansight.Annotations` package documentation for configuration and native Android activity handling.

Offline capture is also bundled but does not initialize or start automatically.
Use `OfflineCapture.Configure(...)` for retained local sessions, ZIP/AES export,
annotation storage, and team upload. See the `Ansight.OfflineCapture` package
documentation.

`WithAnsightSdk(...)` configures FPS sampling, 400ms sampling, 120s retention, 2000ms/quality-60/max-width-480 JPEG capture, platform QR enrollment, host auto-probe, all non-MAUI tools, and full tool access. Host auto-probe remembers successful host sessions and retries the stored registration so the app can reconnect after the host disappears and later reappears. Its callback receives the existing `Options.OptionsBuilder` after runtime defaults and before default tool-suite registration:

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and transported. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

```csharp
using Ansight;
using Ansight.Tools.SecureStorage;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithSecureStorageTools(secure =>
        {
            secure.WithStorageIdentifier("ExampleApp");
            secure.AllowKeyPrefix("ansight.secure.");
        });
    })
    .Build();
```

When the callback registers a tool suite, the all-in-one skips its default registration for that suite, so secure-storage and preferences access can be granted in the same builder call. Full tool access is applied before the callback, which lets the callback override the guard with `WithReadOnlyToolAccess()`, `WithReadWriteToolAccess()`, or `WithToolGuard(...)`.

Configure remembered host profile expiry in the same callback when the default 14 day retention is not appropriate:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithHostConnectionProfileRetention(TimeSpan.FromDays(30));
    })
    .Build();
```

Remote tool scanning is controlled by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when tool packages are included. Because this all-in-one package intentionally includes remote tools, `Disallowed` will fail builds that reference it. Use `Ansight.Core` plus fine-grained `Ansight.Tools.*` references when you need protected Release or CI builds that exercise `Disallowed`. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
