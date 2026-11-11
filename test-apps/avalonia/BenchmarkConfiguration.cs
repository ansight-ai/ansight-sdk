using System.Reflection;
using Ansight;
using Ansight.Connectivity;
using Ansight.Motion;

namespace Ansight.Avalonia.Benchmarks;

public static class BenchmarkConfiguration
{
    public static void Configure(Options.OptionsBuilder options, Assembly applicationAssembly)
    {
        options
            .WithBundledHostConnection(applicationAssembly)
            .WithSessionJpegCapture(
                intervalMilliseconds: 1000,
                quality: 70,
                maxWidth: 720,
                captureGpuBackedSurfaces: true,
                mode: SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch,
                captureKeyboardPresence: true)
            .WithTouchCapture()
            .WithBatteryLevel()
            .WithOpenFileHandleTracking()
            .WithNetworkCapture(network => network.WithBinaryBodies().WithMaximumBodyBytes(262144))
            .WithCrashCapture()
            .WithConnectivityCapture()
            .WithMotionCapture()
            .WithOfflineCapture();

#if ANSIGHT_BENCHMARK_TOOLS
        options.WithBenchmarkFixtures(applicationAssembly);
#endif

#if ANDROID
        options.WithJniReferenceCountTracking();
#endif
    }
}
