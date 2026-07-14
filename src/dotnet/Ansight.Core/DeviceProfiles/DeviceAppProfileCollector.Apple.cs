#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private static void PopulateAppleDeviceProfile(DeviceProfile profile, bool isDesktop, string osName)
    {
        profile.NativeDeviceId = ResolveSimulatorEnvironmentValue("SIMULATOR_UDID");
        profile.Manufacturer = "Apple";
        profile.Brand = "Apple";
        profile.Model = NullIfWhiteSpace(UIDevice.CurrentDevice.Model);
        profile.FormFactor = ResolveAppleFormFactor(isDesktop);
        profile.DeviceClassCode = ResolveAppleOrAndroidDeviceClassCode(isDesktop);
        SetVirtualDeviceState(profile, ResolveAppleIsVirtual());
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
        profile.Icon = ResolveAppleApplicationIcon();
    }

    private static string ResolveAppleFormFactor(bool isDesktop)
    {
        if (isDesktop)
        {
            return DeviceFormFactors.Desktop;
        }

        return UIDevice.CurrentDevice.UserInterfaceIdiom switch
        {
            UIUserInterfaceIdiom.Phone => DeviceFormFactors.Phone,
            UIUserInterfaceIdiom.Pad => DeviceFormFactors.Tablet,
            UIUserInterfaceIdiom.TV => DeviceFormFactors.Tv,
            UIUserInterfaceIdiom.CarPlay => DeviceFormFactors.Car,
            UIUserInterfaceIdiom.Mac => DeviceFormFactors.Desktop,
            _ => DeviceFormFactors.Unknown
        };
    }

    private static bool ResolveAppleIsVirtual()
    {
#if IOS
        if (ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR)
        {
            return true;
        }
#endif

        var model = UIDevice.CurrentDevice.Model ?? string.Empty;
        var localizedModel = UIDevice.CurrentDevice.LocalizedModel ?? string.Empty;
        return HasSimulatorEnvironmentValue("SIMULATOR_DEVICE_NAME")
               || HasSimulatorEnvironmentValue("SIMULATOR_MODEL_IDENTIFIER")
               || HasSimulatorEnvironmentValue("SIMULATOR_UDID")
               || model.Contains("Simulator", StringComparison.OrdinalIgnoreCase)
               || localizedModel.Contains("Simulator", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSimulatorEnvironmentValue(string name)
    {
        return ResolveSimulatorEnvironmentValue(name) is not null;
    }

    private static string? ResolveSimulatorEnvironmentValue(string name)
        => NullIfWhiteSpace(global::System.Environment.GetEnvironmentVariable(name))
           ?? NullIfWhiteSpace(NSProcessInfo.ProcessInfo.Environment[name]?.ToString());

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

    private static DeviceApplicationIconProfile? ResolveAppleApplicationIcon()
    {
        foreach (var iconName in EnumerateAppleApplicationIconNames())
        {
            try
            {
                using var image = UIImage.FromBundle(iconName);
                if (image is null)
                {
                    continue;
                }

                var icon = CreateAppleApplicationIconProfile(image);
                if (icon is not null)
                {
                    return icon;
                }
            }
            catch
            {
                // Continue through the available bundle icon names.
            }
        }

        return null;
    }

    private static DeviceApplicationIconProfile? CreateAppleApplicationIconProfile(UIImage image)
    {
        var sourceWidth = image.CGImage is null
            ? (int)Math.Round((double)image.Size.Width)
            : (int)image.CGImage.Width;
        var sourceHeight = image.CGImage is null
            ? (int)Math.Round((double)image.Size.Height)
            : (int)image.CGImage.Height;
        using (var data = image.AsPNG())
        {
            if (data is null)
            {
                return null;
            }

            return CreateApplicationIconProfile(
                data.ToArray(),
                "png",
                "image/png",
                sourceWidth,
                sourceHeight);
        }
    }

    private static IEnumerable<string> EnumerateAppleApplicationIconNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var iconName in EnumerateAppleApplicationIconNamesCore())
        {
            if (!string.IsNullOrWhiteSpace(iconName) && seen.Add(iconName.Trim()))
            {
                yield return iconName.Trim();
            }
        }
    }

    private static IEnumerable<string?> EnumerateAppleApplicationIconNamesCore()
    {
        var bundle = NSBundle.MainBundle;
        yield return ReadBundleString("CFBundleIconName");
        yield return ReadBundleString("CFBundleIconFile");

        if (bundle.InfoDictionary?["XSAppIconAssets"] is NSString appIconAssets)
        {
            var appIconName = Path.GetFileNameWithoutExtension(appIconAssets.ToString());
            yield return appIconName;
        }

        if (bundle.InfoDictionary?["CFBundleIcons"] is NSDictionary bundleIcons
            && bundleIcons["CFBundlePrimaryIcon"] is NSDictionary primaryIcon)
        {
            if (primaryIcon["CFBundleIconName"] is NSString primaryIconName)
            {
                yield return primaryIconName.ToString();
            }

            if (primaryIcon["CFBundleIconFiles"] is NSArray iconFiles)
            {
                for (nuint index = 0; index < iconFiles.Count; index++)
                {
                    if (iconFiles.GetItem<NSString>(index) is { } iconFile)
                    {
                        yield return iconFile.ToString();
                    }
                }
            }
        }

        yield return "appicon";
        yield return "AppIcon";
    }
}
#endif
