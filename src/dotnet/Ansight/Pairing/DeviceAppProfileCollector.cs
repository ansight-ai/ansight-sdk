using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
#elif IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace Ansight.Pairing;

internal static class DeviceAppProfileCollector
{
    public static DeviceAppProfile Create()
    {
        return new DeviceAppProfile
        {
            SentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ReasonCode = 1,
            ProfileSeq = 1,
            Device = CreateDeviceProfile(),
            App = CreateApplicationProfile(),
            Runtime = CreateRuntimeProfile(),
            Tags =
            [
                "ansight-baseline-profile"
            ]
        };
    }

    private static DeviceProfile CreateDeviceProfile()
    {
        var profile = new DeviceProfile
        {
            Locale = NullIfWhiteSpace(CultureInfo.CurrentUICulture.Name),
            TimeZone = NullIfWhiteSpace(TimeZoneInfo.Local.Id),
            CpuArch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CpuCoreCount = global::System.Environment.ProcessorCount
        };

#if ANDROID
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
        profile.AbiList = Build.SupportedAbis?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
        profile.Display = CreateAndroidDisplayProfile();
        profile.Battery = CreateAndroidBatteryProfile();
#elif IOS || MACCATALYST
        profile.Manufacturer = "Apple";
        profile.Brand = "Apple";
        profile.Model = NullIfWhiteSpace(UIDevice.CurrentDevice.Model);
        profile.DeviceClassCode = ResolveAppleOrAndroidDeviceClassCode(isDesktop: UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Mac);
        profile.OsName = ResolveAppleOsName();
        profile.OsVersion = NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
        profile.OsBuild = NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
        profile.Display = CreateAppleDisplayProfile();
#else
        profile.OsName = ResolveFallbackOsName();
        profile.OsVersion = NullIfWhiteSpace(global::System.Environment.OSVersion.VersionString);
#endif

        return profile;
    }

    private static DeviceApplicationProfile CreateApplicationProfile()
    {
        var assemblyVersion = ResolveAssemblyVersion();
        var profile = new DeviceApplicationProfile
        {
            EnvironmentCode = ResolveEnvironmentCode(),
            Debuggable = ResolveIsDebuggable(),
            VersionName = assemblyVersion,
            VersionCode = assemblyVersion,
            BuildNumber = assemblyVersion
        };

#if ANDROID
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
#elif IOS || MACCATALYST
        profile.AppId = NullIfWhiteSpace(NSBundle.MainBundle.BundleIdentifier);
        profile.AppName = ReadBundleString("CFBundleDisplayName")
                          ?? ReadBundleString("CFBundleName")
                          ?? profile.AppId;
        profile.VersionName = ReadBundleString("CFBundleShortVersionString") ?? profile.VersionName;
        profile.VersionCode = ReadBundleString("CFBundleVersion") ?? profile.VersionCode;
        profile.BuildNumber = profile.VersionCode;
#else
        profile.AppId = NullIfWhiteSpace(Assembly.GetEntryAssembly()?.GetName().Name);
        profile.AppName = profile.AppId;
#endif

        return profile;
    }

    private static DeviceRuntimeProfile CreateRuntimeProfile()
    {
        return new DeviceRuntimeProfile
        {
            Primary = 250,
            PrimaryVersion = ResolveAssemblyVersion(),
            Engine = new DeviceRuntimeEngineProfile
            {
                Name = "dotnet",
                Version = global::System.Environment.Version.ToString()
            },
            Stack =
            [
                new DeviceRuntimeStackEntry
                {
                    RuntimeCode = 250,
                    Name = "dotnet",
                    Version = global::System.Environment.Version.ToString()
                },
                new DeviceRuntimeStackEntry
                {
                    RuntimeCode = ResolvePlatformRuntimeCode(),
                    Name = ResolveRuntimePlatformName(),
                    Version = ResolvePlatformVersion()
                }
            ],
            AotEnabled = !RuntimeFeature.IsDynamicCodeCompiled,
            JitEnabled = RuntimeFeature.IsDynamicCodeSupported
        };
    }

    private static string? ResolveAssemblyVersion()
    {
        return NullIfWhiteSpace(Assembly.GetEntryAssembly()?.GetName().Version?.ToString())
               ?? NullIfWhiteSpace(Assembly.GetExecutingAssembly().GetName().Version?.ToString());
    }

    private static int ResolveEnvironmentCode()
    {
#if DEBUG
        return 2;
#else
        return 1;
#endif
    }

    private static bool ResolveIsDebuggable()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static int ResolvePlatformRuntimeCode()
    {
#if ANDROID
        return 1;
#elif IOS
        return 2;
#elif MACCATALYST
        return 3;
#else
        return 0;
#endif
    }

    private static string ResolveRuntimePlatformName()
    {
#if ANDROID
        return "android";
#elif IOS
        return "ios";
#elif MACCATALYST
        return "maccatalyst";
#else
        return ResolveFallbackOsName();
#endif
    }

    private static string? ResolvePlatformVersion()
    {
#if ANDROID
        return NullIfWhiteSpace(Build.VERSION.Release);
#elif IOS || MACCATALYST
        return NullIfWhiteSpace(UIDevice.CurrentDevice.SystemVersion);
#else
        return NullIfWhiteSpace(global::System.Environment.OSVersion.VersionString);
#endif
    }

    private static string ResolveFallbackOsName()
    {
        if (OperatingSystem.IsMacCatalyst())
        {
            return "maccatalyst";
        }

        if (OperatingSystem.IsIOS())
        {
            return "ios";
        }

        if (OperatingSystem.IsAndroid())
        {
            return "android";
        }

        return global::System.Environment.OSVersion.Platform.ToString().ToLowerInvariant();
    }

    private static int ResolveAppleOrAndroidDeviceClassCode(bool isDesktop)
    {
        return isDesktop ? 3 : 1;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

#if ANDROID
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
#endif

#if IOS || MACCATALYST
    private static string ResolveAppleOsName()
    {
#if MACCATALYST
        return "maccatalyst";
#else
        return "ios";
#endif
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
#endif
}
