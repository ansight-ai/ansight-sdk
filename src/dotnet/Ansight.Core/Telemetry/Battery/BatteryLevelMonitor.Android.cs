#if ANDROID
using System;
using Android.App;
using Android.Content;
using Android.OS;

namespace Ansight.Telemetry.Battery;

internal sealed class BatteryLevelMonitor : IBatteryLevelMonitor
{
    public bool IsSupported => Application.Context is not null;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public long? ReadBatteryLevelPercentage()
    {
        var context = Application.Context;
        if (context is null)
        {
            return null;
        }

        using var batteryIntent = context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
        if (batteryIntent is null)
        {
            return null;
        }

        var level = batteryIntent.GetIntExtra(BatteryManager.ExtraLevel, -1);
        var scale = batteryIntent.GetIntExtra(BatteryManager.ExtraScale, -1);
        if (level < 0 || scale <= 0)
        {
            return null;
        }

        return Math.Clamp((long)Math.Round(level * 100d / scale), 0, 100);
    }

    public void Dispose()
    {
    }
}
#endif
