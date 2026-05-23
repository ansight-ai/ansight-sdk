#if ANDROID
using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;

namespace Ansight.DeviceProfiles;

internal static partial class DeviceAppProfileCollector
{
    private const int AndroidScreenLayoutSizeLarge = 0x03;
    private const int AndroidScreenLayoutSizeMask = 0x0f;
    private const int AndroidUiModeTypeCar = 0x03;
    private const int AndroidUiModeTypeMask = 0x0f;
    private const int AndroidUiModeTypeTelevision = 0x04;
    private const int AndroidUiModeTypeVrHeadset = 0x07;
    private const int AndroidUiModeTypeWatch = 0x06;

    private static partial void PopulateDeviceProfile(DeviceProfile profile)
    {
        profile.Manufacturer = NullIfWhiteSpace(Build.Manufacturer);
        profile.Brand = NullIfWhiteSpace(Build.Brand);
        profile.Model = NullIfWhiteSpace(Build.Model);
        profile.Product = NullIfWhiteSpace(Build.Product);
        profile.FormFactor = ResolveAndroidFormFactor();
        profile.DeviceClassCode = ResolveAppleOrAndroidDeviceClassCode(isDesktop: false);
        SetVirtualDeviceState(profile, ResolveAndroidIsEmulator());
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
        profile.Icon = ResolveAndroidApplicationIcon(packageManager, packageName);
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

    private static string? ResolveAndroidFormFactor()
    {
        var configuration = Application.Context?.Resources?.Configuration;
        if (configuration is null)
        {
            return null;
        }

        var uiModeType = (int)configuration.UiMode & AndroidUiModeTypeMask;
        if (uiModeType == AndroidUiModeTypeCar)
        {
            return DeviceFormFactors.Car;
        }

        if (uiModeType == AndroidUiModeTypeTelevision)
        {
            return DeviceFormFactors.Tv;
        }

        if (uiModeType == AndroidUiModeTypeWatch)
        {
            return DeviceFormFactors.Watch;
        }

        if (uiModeType == AndroidUiModeTypeVrHeadset)
        {
            return DeviceFormFactors.Vr;
        }

        var screenLayoutSize = (int)configuration.ScreenLayout & AndroidScreenLayoutSizeMask;
        return screenLayoutSize >= AndroidScreenLayoutSizeLarge
            ? DeviceFormFactors.Tablet
            : DeviceFormFactors.Phone;
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

    private static DeviceApplicationIconProfile? ResolveAndroidApplicationIcon(PackageManager? packageManager, string? packageName)
    {
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        try
        {
            using var drawable = packageManager.GetApplicationIcon(packageName);
            return drawable is null ? null : CreateAndroidApplicationIconProfile(drawable);
        }
        catch
        {
            return null;
        }
    }

    private static DeviceApplicationIconProfile? CreateAndroidApplicationIconProfile(Drawable drawable)
    {
        var sourceWidth = drawable.IntrinsicWidth > 0 ? drawable.IntrinsicWidth : MaxApplicationIconPixelLength;
        var sourceHeight = drawable.IntrinsicHeight > 0 ? drawable.IntrinsicHeight : MaxApplicationIconPixelLength;
        var dimensions = ResolveApplicationIconDimensions(sourceWidth, sourceHeight);
        var bitmapConfig = Bitmap.Config.Argb8888;
        if (bitmapConfig is null)
        {
            return null;
        }

        using var bitmap = Bitmap.CreateBitmap(dimensions.Width, dimensions.Height, bitmapConfig);
        if (bitmap is null)
        {
            return null;
        }

        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, dimensions.Width, dimensions.Height);
        drawable.Draw(canvas);

        using var stream = new MemoryStream();
        var pngFormat = Bitmap.CompressFormat.Png;
        if (pngFormat is null || !bitmap.Compress(pngFormat, 100, stream))
        {
            return null;
        }

        return CreateApplicationIconProfile(
            stream.ToArray(),
            "png",
            "image/png",
            dimensions.Width,
            dimensions.Height);
    }
}
#endif
