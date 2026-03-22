using Foundation;
using UIKit;

namespace Ansight.TestHarness;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnActivated(UIApplication application)
    {
        base.OnActivated(application);
        Runtime.SetAppLifecycleState(AppLifecycleState.Foreground);
    }

    public override void DidEnterBackground(UIApplication application)
    {
        base.DidEnterBackground(application);
        Runtime.SetAppLifecycleState(AppLifecycleState.Background);
    }
}
