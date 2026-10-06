# Ansight.Motion

App-fed shake and accelerometer evidence for Ansight .NET applications.

Enable motion capture when building the runtime options:

```csharp
using Ansight;
using Ansight.Motion;

var options = Options.CreateBuilder()
    .WithMotionCapture()
    .Build();
```

Forward motion from the app's existing handlers:

```csharp
MotionCapture.RecordShake();
MotionCapture.RecordAccelerometer(x, y, z);
```

Accelerometer values must be in metres per second squared. Samples are limited
to one every 20 ms by default. The SDK does not subscribe to sensors or request
sensor permissions. Calls made while the runtime is inactive are ignored.

See the [motion capture guide](https://github.com/ansight-ai/ansight-sdk/blob/main/docs/motion.md)
for platform guidance and configuration options.
