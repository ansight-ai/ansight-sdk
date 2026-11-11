using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Ansight.Avalonia.Tests.TestApplication))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Ansight.Avalonia.Tests;

public sealed class TestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
