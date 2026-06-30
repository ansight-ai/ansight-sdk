using Ansight.Maui;
using Microsoft.Extensions.Logging;

namespace Ansight.OfflineCapture.MauiSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseAnsight<App>(ansight =>
            {
                ansight.WithAdditionalLogger(new SampleAnsightLogCallback());
                ansight.WithAdditionalChannels(SampleAnsightConfiguration.AdditionalChannels);
                ansight.WithSampleFrequencyMilliseconds(250);
                ansight.WithRetentionPeriodSeconds(180);
                ansight.WithSessionJpegCapture(1000, 55, 540);
                ansight.RegisterCustomProperty("sample", "app", "offline-capture-maui");
                ansight.RegisterCustomProperty("sample", "build", "debug");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
