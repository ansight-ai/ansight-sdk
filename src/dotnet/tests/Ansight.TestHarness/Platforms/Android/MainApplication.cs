using Android.App;
using Android.Runtime;
using Ansight.Platforms.Android;

namespace Ansight.TestHarness;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();
        AndroidAppLifecycleTracker.Register(this);
    }
}
