using Android.App;
using Android.Runtime;

namespace Ansight.TestHarness;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        var options = Options.CreateBuilder()
            .WithAdditionalLogger(new CustomAnsightLogCallback())
            .WithMauiWindowProvider()
            .WithShakeGesture()
            .WithShakeGestureBehaviour(ShakeGestureBehaviour.Overlay)
            .WithShakeGesturePredicate(() => ShakePredicateCoordinator.ShouldAllowShake)
            .WithAdditionalChannels(CustomAnsightConfiguration.AdditionalChannels)
            .WithSaveSnapshotAction(SnapshotActionHelper.CopySnapshotToClipboardAsync, "COPY")
            .Build();
        
        Runtime.InitializeAndActivate(options);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
