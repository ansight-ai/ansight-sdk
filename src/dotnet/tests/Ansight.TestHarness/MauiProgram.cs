using Ansight.Maui;
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
        HarnessReflectionRoots.Register();

        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseAnsight<App>(ansight =>
            {
                ansight.WithAdditionalLogger(new CustomAnsightLogCallback());
                ansight.WithAdditionalChannels(CustomAnsightConfiguration.AdditionalChannels);
                ansight.WithHostAutoProbe(new HostAutoProbeOptions
                {
                    InitialDelay = TimeSpan.FromSeconds(1),
                    ProbeInterval = TimeSpan.FromSeconds(5),
                    ReconnectDelay = TimeSpan.FromSeconds(10),
                    ClientName = CustomAnsightConfiguration.ClientName
                });
                ansight.WithPreferencesTools(preferences =>
                {
                    preferences.WithDefaultStore(preferencesStore);
                    preferences.AllowStore(preferencesStore);
                    preferences.AllowKeyPrefix("ansight.");
                });
                ansight.WithSecureStorageTools(secure =>
                {
                    secure.WithStorageIdentifier("AnsightHarness");
                    secure.AllowKeyPrefix("ansight.secure.");
                });
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
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
