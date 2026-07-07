using UIKit;

namespace Ansight.TestHarness.iOSNative;

public static class Program
{
    static void Main(string[] args)
    {
        var options = Options.CreateBuilder()
            .WithFramesPerSecond()
            .WithHostAutoProbe(new HostAutoProbeOptions
            {
                InitialDelay = TimeSpan.FromSeconds(1),
                ProbeInterval = TimeSpan.FromSeconds(5),
                ReconnectDelay = TimeSpan.FromSeconds(10),
                ClientName = "Ansight .NET iOS Native Harness"
            })
            .Build();

        Runtime.InitializeAndActivate(options);
        
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
