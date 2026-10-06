using UIKit;

namespace Ansight.TestHarness.iOSNative;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);

        var root = new HarnessViewController();
        Window.RootViewController = root;
        Window.MakeKeyAndVisible();

        var options = Options.CreateBuilder()
            .WithAnsightSdk()
            .WithHostAutoProbe(new HostAutoProbeOptions
            {
                InitialDelay = TimeSpan.FromSeconds(1),
                ProbeInterval = TimeSpan.FromSeconds(5),
                ReconnectDelay = TimeSpan.FromSeconds(10),
                ClientName = "Ansight .NET iOS Native Harness"
            })
            .Build();

        Runtime.InitializeAndActivate(options);

        return true;
    }
}
