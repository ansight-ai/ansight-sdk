#if MACCATALYST
using UIKit;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private static partial void PopulateDeviceProfile(DeviceProfile profile)
    {
        PopulateAppleDeviceProfile(
            profile,
            isDesktop: UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Mac,
            osName: "maccatalyst");
    }

    private static partial void PopulateApplicationProfile(DeviceApplicationProfile profile)
    {
        PopulateAppleApplicationProfile(profile);
    }

    private static partial int ResolvePlatformRuntimeCode() => 3;

    private static partial string ResolveRuntimePlatformName() => "maccatalyst";

    private static partial string? ResolvePlatformVersion() => NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
}
#endif
