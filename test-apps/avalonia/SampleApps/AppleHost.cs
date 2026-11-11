using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace Ansight.Avalonia.Benchmarks.Samples;

[Register("AppDelegate")]
public sealed class DemoAppDelegate : AvaloniaAppDelegate<DemoApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder).UseAnsight<DemoApp>(options =>
            BenchmarkConfiguration.Configure(options, typeof(DemoAppDelegate).Assembly));
    }
}

internal static class Program
{
    private static void Main(string[] arguments)
    {
        UIApplication.Main(arguments, null, typeof(DemoAppDelegate));
    }
}
