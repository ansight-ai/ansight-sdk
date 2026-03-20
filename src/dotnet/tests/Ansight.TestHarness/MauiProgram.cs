using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Ansight.TestHarness;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        var ansightOptions = Options.CreateBuilder()
            .WithAdditionalLogger(new CustomAnsightLogCallback())
            .WithFramesPerSecond()
            .WithSampleFrequencyMilliseconds(400)
            .WithRetentionPeriodSeconds(120)
            .WithAdditionalChannels(CustomAnsightConfiguration.AdditionalChannels)
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
}
