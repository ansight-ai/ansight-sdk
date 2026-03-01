using UIKit;

namespace Ansight.TestHarness.iOSNative;

public static class Program
{
    static void Main(string[] args)
    {
        Runtime.InitializeAndActivate();
        
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
