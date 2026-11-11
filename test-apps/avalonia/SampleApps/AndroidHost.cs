using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Ansight.Avalonia.Benchmarks.Samples;

[Application(UsesCleartextTraffic = true)]
public sealed class DemoAndroidApplication : AvaloniaAndroidApplication<DemoApp>
{
    public DemoAndroidApplication(nint reference, JniHandleOwnership ownership) : base(reference, ownership)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).UseAnsight<DemoApp>(options =>
            BenchmarkConfiguration.Configure(options, typeof(DemoAndroidApplication).Assembly));
    }
}

[Activity(Theme = "@android:style/Theme.Material.Light.NoActionBar", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class DemoActivity : AvaloniaMainActivity
{
}
