using Foundation;
using UIKit;

namespace Ansight.TestHarness.MacCatalystNative;

[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
        {
            return;
        }

        var window = new UIWindow(windowScene);
        var root = new HarnessViewController();
        window.RootViewController = root;
        window.MakeKeyAndVisible();

        Window = window;

        var options = Options.CreateBuilder()
            .WithAnsightSdk()
            .WithHostAutoProbe(new HostAutoProbeOptions
            {
                InitialDelay = TimeSpan.FromSeconds(1),
                ProbeInterval = TimeSpan.FromSeconds(5),
                ReconnectDelay = TimeSpan.FromSeconds(10),
                ClientName = "Ansight .NET Mac Catalyst Native Harness"
            })
            .Build();

        Runtime.InitializeAndActivate(options);
    }
}
