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
            .WithShakeGesture()
            .WithMauiWindowProvider()
            .WithFramesPerSecond()
            .WithSampleFrequencyMilliseconds(400)
            .WithRetentionPeriodSeconds(120)
            .WithShakeGestureBehaviour(ShakeGestureBehaviour.Overlay)
            .WithShakeGesturePredicate(() => ShakePredicateCoordinator.ShouldAllowShake)
            .WithAdditionalChannels(CustomAnsightConfiguration.AdditionalChannels)
            .WithSaveSnapshotAction(SnapshotActionHelper.CopySnapshotToClipboardAsync, "COPY")
            .Build();

        builder
            .UseMauiApp<App>()
            .UseAnsight(ansightOptions)
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

        // Activate after platform services are registered by UseAnsight.
        Runtime.Activate();

        return app;
    }
}
