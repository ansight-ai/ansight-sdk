using System;
using System.Linq;
#if ANDROID
using Android.App;
#elif IOS || MACCATALYST
using UIKit;
#endif

namespace Ansight;

/// <summary>
/// Ensures platform-specific presentation + FPS services are registered before the runtime is created.
/// </summary>
internal static class PlatformBootstrapper
{
    private static bool configured;

    public static void EnsureConfigured(Options options)
    {
        if (configured)
        {
            return;
        }

#if ANDROID
        var provider = options.PresentationWindowProvider
            ?? throw new InvalidOperationException("Options.PresentationWindowProvider is required on Android. Use WithPresentationWindowProvider/WithMauiWindowProvider.");

        RuntimePlatform.Configure(() => (Activity?)provider());
        RuntimePlatform.RegisterPresentationFactory((o, sink) => new AndroidNativePresentationService(o, sink, () => (Activity?)provider()));
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new AndroidFrameRateMonitor());
#elif IOS
        var provider = options.PresentationWindowProvider ?? GetDefaultWindowProvider();
        RuntimePlatform.Configure(() => (UIKit.UIWindow?)provider());
        RuntimePlatform.RegisterPresentationFactory((o, sink) => new IosNativePresentationService(o, sink, () => (UIKit.UIWindow?)provider()));
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new IosFrameRateMonitor());
#elif MACCATALYST
        var provider = options.PresentationWindowProvider ?? GetDefaultWindowProvider();
        RuntimePlatform.Configure(() => (UIKit.UIWindow?)provider());
        RuntimePlatform.RegisterPresentationFactory((o, sink) => new MacCatalystNativePresentationService(o, sink, () => (UIKit.UIWindow?)provider()));
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new MacCatalystFrameRateMonitor());
#endif

        configured = true;
    }

#if IOS || MACCATALYST
    private static Func<object?> GetDefaultWindowProvider() => () =>
        UIKit.UIApplication.SharedApplication
            ?.ConnectedScenes
            ?.OfType<UIKit.UIWindowScene>()
            ?.SelectMany(scene => scene.Windows)
            ?.FirstOrDefault(w => w.IsKeyWindow)
        ?? UIKit.UIApplication.SharedApplication
            ?.ConnectedScenes
            ?.OfType<UIKit.UIWindowScene>()
            ?.SelectMany(scene => scene.Windows)
            ?.FirstOrDefault();
#endif
}
