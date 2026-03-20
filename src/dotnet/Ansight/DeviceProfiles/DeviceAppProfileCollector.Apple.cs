#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private static void PopulateAppleDeviceProfile(DeviceProfile profile, bool isDesktop, string osName)
    {
        profile.Manufacturer = "Apple";
        profile.Brand = "Apple";
        profile.Model = NullIfWhiteSpace(UIDevice.CurrentDevice.Model);
        profile.DeviceClassCode = ResolveAppleOrAndroidDeviceClassCode(isDesktop);
        profile.OsName = osName;
        profile.OsVersion = NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
        profile.OsBuild = NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
        profile.Display = CreateAppleDisplayProfile();
    }

    private static void PopulateAppleApplicationProfile(DeviceApplicationProfile profile)
    {
        profile.AppId = NullIfWhiteSpace(NSBundle.MainBundle.BundleIdentifier);
        profile.AppName = ReadBundleString("CFBundleDisplayName")
                          ?? ReadBundleString("CFBundleName")
                          ?? profile.AppId;
        profile.VersionName = ReadBundleString("CFBundleShortVersionString") ?? profile.VersionName;
        profile.VersionCode = ReadBundleString("CFBundleVersion") ?? profile.VersionCode;
        profile.BuildNumber = profile.VersionCode;
    }

    private static DeviceDisplayProfile? CreateAppleDisplayProfile()
    {
        try
        {
            var screen = UIScreen.MainScreen;
            if (screen is null)
            {
                return null;
            }

            return new DeviceDisplayProfile
            {
                WidthPx = (int)Math.Round(screen.Bounds.Width * screen.Scale),
                HeightPx = (int)Math.Round(screen.Bounds.Height * screen.Scale),
                DensityDpi = (int)Math.Round(screen.Scale * 160d),
                RefreshRateHz = screen.MaximumFramesPerSecond > 0
                    ? screen.MaximumFramesPerSecond
                    : null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadBundleString(string key)
    {
        try
        {
            return NullIfWhiteSpace(NSBundle.MainBundle.ObjectForInfoDictionary(key)?.ToString());
        }
        catch
        {
            return null;
        }
    }
}
#endif
