#if !ANDROID && !IOS && !MACCATALYST
using System.Reflection;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private static partial void PopulateDeviceProfile(DeviceProfile profile)
    {
        profile.OsName = ResolveFallbackOsName();
        profile.OsVersion = NullIfWhiteSpace(global::System.Environment.OSVersion.VersionString);
    }

    private static partial void PopulateApplicationProfile(DeviceApplicationProfile profile)
    {
        profile.AppId = NullIfWhiteSpace(Assembly.GetEntryAssembly()?.GetName().Name);
        profile.AppName = profile.AppId;
    }

    private static partial int ResolvePlatformRuntimeCode() => 0;

    private static partial string ResolveRuntimePlatformName() => ResolveFallbackOsName();

    private static partial string? ResolvePlatformVersion() => NullIfWhiteSpace(global::System.Environment.OSVersion.VersionString);
}
#endif
