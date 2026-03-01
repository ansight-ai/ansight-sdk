using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;

namespace Ansight;

/// <summary>
/// Convenience helpers for wiring native window handles from a MAUI app into Ansight options.
/// </summary>
public static class MauiAnsightOptionsBuilderExtensions
{
    /// <summary>
    /// Uses the current MAUI window/activity handle as the presentation window provider.
    /// </summary>
    public static Options.OptionsBuilder WithMauiWindowProvider(this Options.OptionsBuilder builder)
    {
#if ANDROID
        builder.WithPresentationWindowProvider(() => Platform.CurrentActivity);
#elif IOS || MACCATALYST
        builder.WithPresentationWindowProvider(() =>
        {
            return UIKit.UIApplication.SharedApplication
                       ?.ConnectedScenes
                       ?.OfType<UIKit.UIWindowScene>()
                       ?.SelectMany(scene => scene.Windows)
                       ?.FirstOrDefault(w => w.IsKeyWindow)
                   ?? UIKit.UIApplication.SharedApplication
                       ?.ConnectedScenes
                       ?.OfType<UIKit.UIWindowScene>()
                       ?.SelectMany(scene => scene.Windows)
                       ?.FirstOrDefault();
        });
#else
        builder.WithPresentationWindowProvider(() => null);
#endif
        return builder;
    }
}
