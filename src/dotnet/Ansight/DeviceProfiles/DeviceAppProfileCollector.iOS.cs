#if IOS
using UIKit;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private static partial void PopulateDeviceProfile(DeviceProfile profile)
    {
        PopulateAppleDeviceProfile(profile, isDesktop: false, osName: "ios");
    }

    private static partial void PopulateApplicationProfile(DeviceApplicationProfile profile)
    {
        PopulateAppleApplicationProfile(profile);
    }

    private static partial int ResolvePlatformRuntimeCode() => 2;

    private static partial string ResolveRuntimePlatformName() => "ios";

    private static partial string? ResolvePlatformVersion() => NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
}
#endif
