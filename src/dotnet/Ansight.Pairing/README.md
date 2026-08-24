# Ansight.Pairing

`Ansight.Pairing` adds package-owned native enrollment acquisition to
`Ansight.Core`.

It provides:

- native QR scanning for `HostConnectionRequest.QrCode(...)`
- automatic Android activity tracking and native Apple modal presentation
- a default `IHostConnectionConfigReader` wired into the existing runtime-owned host connection flow

The `Ansight` and `Ansight.Maui` all-in-one packages include this package where supported.

```sh
dotnet add package Ansight.Pairing --prerelease
```

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Usage

Start the local host with `ansight host run`. Host-local runtimes register
automatically and do not need this UI. For a physical device, issue a one-use
QR:

```sh
ansight pairing issue --qr
```

Open the reader only from a developer-only app surface:

```csharp
using Ansight;

var optionsBuilder = Options.CreateBuilder()
    .WithPlatformPairing();

var options = optionsBuilder.Build();

Runtime.InitializeAndActivate(options);
await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.QrCode());
```

On Android, the package tracks the resumed `Activity` itself. The overload that
accepts an activity provider remains available for unusual host frameworks.

Once enabled, the app can keep using the existing host connection requests:

```csharp
await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.QrCode());
await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.Auto());
await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.PayloadText(payload));
```

`QrCode()` is the normal physical-device first-use flow. It registers a random
app-installation id using the one-use host invite. `Auto()` and host auto-probe
then use the saved registration for reconnect. `PayloadText(...)` is for apps
that already own the scanner; a bundled pairing file is not part of normal
setup.

See the [.NET enrollment guide](https://www.ansight.ai/docs/sdk/dotnet/pairing)
for the complete physical-device flow.

Configure profile retention through the core options builder:

```csharp
var options = Options.CreateBuilder()
    .WithHostConnectionProfileRetention(TimeSpan.FromDays(30))
    .Build();
```
