#if IOS || MACCATALYST
using System;
using Foundation;
using UIKit;

namespace Ansight.Telemetry.Battery;

internal sealed class BatteryLevelMonitor : NSObject, IBatteryLevelMonitor
{
    private readonly object sync = new object();
    private bool running;
    private bool previousBatteryMonitoringEnabled;

    public bool IsSupported => true;

    public void Start()
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            lock (sync)
            {
                if (running)
                {
                    return;
                }

                var device = UIDevice.CurrentDevice;
                previousBatteryMonitoringEnabled = device.BatteryMonitoringEnabled;
                device.BatteryMonitoringEnabled = true;
                running = true;
            }
        });
    }

    public void Stop()
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            lock (sync)
            {
                if (!running)
                {
                    return;
                }

                UIDevice.CurrentDevice.BatteryMonitoringEnabled = previousBatteryMonitoringEnabled;
                running = false;
            }
        });
    }

    public long? ReadBatteryLevelPercentage()
    {
        double batteryLevel = -1;
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            batteryLevel = (double)UIDevice.CurrentDevice.BatteryLevel;
        });

        if (batteryLevel < 0)
        {
            return null;
        }

        return Math.Clamp((long)Math.Round(batteryLevel * 100d), 0, 100);
    }

    protected override void Dispose(bool disposing)
    {
        Stop();
        base.Dispose(disposing);
    }
}
#endif
