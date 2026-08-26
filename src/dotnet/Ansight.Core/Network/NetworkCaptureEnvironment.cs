using System.Runtime.InteropServices;

#if ANDROID
using Android.OS;
#endif

namespace Ansight.Network;

internal static class NetworkCaptureEnvironment
{
    public static bool IsSimulatorOrEmulator
    {
        get
        {
#if ANDROID
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
                   || brand.StartsWith("generic", StringComparison.OrdinalIgnoreCase)
                      && device.StartsWith("generic", StringComparison.OrdinalIgnoreCase)
                   || product.Contains("sdk", StringComparison.OrdinalIgnoreCase);
#elif IOS
            return RuntimeInformation.RuntimeIdentifier.Contains(
                       "iossimulator",
                       StringComparison.OrdinalIgnoreCase)
                   || !string.IsNullOrWhiteSpace(
                       Environment.GetEnvironmentVariable("SIMULATOR_DEVICE_NAME"));
#else
            return false;
#endif
        }
    }
}
