using Ansight.Tools.Preferences;
using Ansight.Tools.SecureStorage;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Ansight.TestHarness;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        var preferencesStore = GetPreferencesStoreName();
        var ansightOptions = Options.CreateBuilder()
            .WithAdditionalLogger(new CustomAnsightLogCallback())
            .WithFramesPerSecond()
            .WithSampleFrequencyMilliseconds(400)
            .WithRetentionPeriodSeconds(120)
            .WithAdditionalChannels(CustomAnsightConfiguration.AdditionalChannels)
            .WithPreferencesTools(preferences =>
            {
                preferences.WithDefaultStore(preferencesStore);
                preferences.AllowStore(preferencesStore);
                preferences.AllowKeyPrefix("ansight.");
            })
            .WithSecureStorageTools(secure =>
            {
                secure.WithStorageIdentifier("AnsightHarness");
                secure.AllowKeyPrefix("ansight.secure.");
            })
            .WithAllToolAccess()
            .Build();

        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        EnsureRuntimeStarted(ansightOptions);
        return app;
    }

    private static void EnsureRuntimeStarted(Options ansightOptions)
    {
        if (!Runtime.IsInitialized)
        {
            Runtime.InitializeAndActivate(ansightOptions);
            return;
        }

        if (!Runtime.IsActive)
        {
            Runtime.Activate();
        }
    }

    private static string GetPreferencesStoreName()
    {
#if ANDROID
        return Microsoft.Maui.ApplicationModel.AppInfo.Current.PackageName + "_preferences";
#else
        return "standard";
#endif
    }
}
