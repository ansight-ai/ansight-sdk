#if ANDROID
using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private static partial void PopulateDeviceProfile(DeviceProfile profile)
    {
        profile.Manufacturer = NullIfWhiteSpace(Build.Manufacturer);
        profile.Brand = NullIfWhiteSpace(Build.Brand);
        profile.Model = NullIfWhiteSpace(Build.Model);
        profile.Product = NullIfWhiteSpace(Build.Product);
        profile.DeviceClassCode = ResolveAppleOrAndroidDeviceClassCode(isDesktop: false);
        profile.IsEmulator = ResolveAndroidIsEmulator();
        profile.OsName = "android";
        profile.OsVersion = NullIfWhiteSpace(Build.VERSION.Release);
        profile.OsBuild = NullIfWhiteSpace(Build.Display);
        profile.ApiLevel = (int)Build.VERSION.SdkInt;
        profile.AbiList = Build.SupportedAbis?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        profile.Display = CreateAndroidDisplayProfile();
        profile.Battery = CreateAndroidBatteryProfile();
    }

    private static partial void PopulateApplicationProfile(DeviceApplicationProfile profile)
    {
        var context = Application.Context;
        var packageName = context?.PackageName;
        profile.AppId = NullIfWhiteSpace(packageName);

        var packageManager = context?.PackageManager;
        if (packageManager is not null && !string.IsNullOrWhiteSpace(packageName))
        {
            try
            {
                var packageInfo = packageManager.GetPackageInfo(packageName, 0);
                profile.VersionName = NullIfWhiteSpace(packageInfo?.VersionName) ?? profile.VersionName;
                profile.VersionCode = packageInfo is null ? profile.VersionCode : ResolveAndroidVersionCode(packageInfo);
                profile.BuildNumber = profile.VersionCode;
            }
            catch
            {
                // Fall back to assembly metadata when package info lookup fails.
            }
        }

        profile.AppName = ResolveAndroidApplicationLabel();
    }

    private static partial int ResolvePlatformRuntimeCode() => 1;

    private static partial string ResolveRuntimePlatformName() => "android";

    private static partial string? ResolvePlatformVersion() => NullIfWhiteSpace(Build.VERSION.Release);

    private static DeviceDisplayProfile? CreateAndroidDisplayProfile()
    {
        var metrics = Application.Context?.Resources?.DisplayMetrics;
        if (metrics is null)
        {
            return null;
        }

        return new DeviceDisplayProfile
        {
            WidthPx = metrics.WidthPixels,
            HeightPx = metrics.HeightPixels,
            DensityDpi = (int)metrics.DensityDpi
        };
    }

    private static DeviceBatteryProfile? CreateAndroidBatteryProfile()
    {
        var context = Application.Context;
        if (context is null)
        {
            return null;
        }

        try
        {
            using var batteryIntent = context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
            if (batteryIntent is null)
            {
                return null;
            }

            var level = batteryIntent.GetIntExtra(BatteryManager.ExtraLevel, -1);
            var scale = batteryIntent.GetIntExtra(BatteryManager.ExtraScale, -1);
            var status = batteryIntent.GetIntExtra(BatteryManager.ExtraStatus, -1);

            return new DeviceBatteryProfile
            {
                LevelPct = level >= 0 && scale > 0
                    ? (int)Math.Round(level * 100d / scale)
                    : null,
                StateCode = ResolveAndroidBatteryStateCode(status)
            };
        }
        catch
        {
            return null;
        }
    }

    private static int? ResolveAndroidBatteryStateCode(int status)
    {
        return status switch
        {
            (int)BatteryStatus.Charging => 2,
            (int)BatteryStatus.Full => 3,
            (int)BatteryStatus.Discharging => 1,
            (int)BatteryStatus.NotCharging => 1,
            _ => 0
        };
    }

    private static bool ResolveAndroidIsEmulator()
    {
        var fingerprint = Build.Fingerprint ?? string.Empty;
        var model = Build.Model ?? string.Empty;
        var product = Build.Product ?? string.Empty;
        var manufacturer = Build.Manufacturer ?? string.Empty;
        var brand = Build.Brand ?? string.Empty;
        var device = Build.Device ?? string.Empty;

        return fingerprint.Contains("generic", StringComparison.OrdinalIgnoreCase)
               || fingerprint.Contains("emulator", StringComparison.OrdinalIgnoreCase)
               || model.Contains("Emulator", StringComparison.OrdinalIgnoreCase)
               || model.Contains("Android SDK built for", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Genymotion", StringComparison.OrdinalIgnoreCase)
               || (brand.StartsWith("generic", StringComparison.OrdinalIgnoreCase)
                   && device.StartsWith("generic", StringComparison.OrdinalIgnoreCase))
               || product.Contains("sdk", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveAndroidApplicationLabel()
    {
        var context = Application.Context;
        var packageManager = context?.PackageManager;
        var applicationInfo = context?.ApplicationInfo;
        if (packageManager is null || applicationInfo is null)
        {
            return null;
        }

        try
        {
            return NullIfWhiteSpace(packageManager.GetApplicationLabel(applicationInfo)?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveAndroidVersionCode(PackageInfo packageInfo)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return packageInfo.LongVersionCode.ToString(CultureInfo.InvariantCulture);
        }

#pragma warning disable CA1422
#pragma warning disable CS0618
        return packageInfo.VersionCode.ToString(CultureInfo.InvariantCulture);
#pragma warning restore CA1422
#pragma warning restore CS0618
    }
}
#endif
