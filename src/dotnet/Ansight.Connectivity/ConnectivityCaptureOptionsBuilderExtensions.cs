using System.Text.Json.Nodes;
using Ansight.Native;

namespace Ansight.Connectivity;

public static class ConnectivityCaptureOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithConnectivityCapture(this Options.OptionsBuilder builder,
        Action<ConnectivityCaptureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ConnectivityCaptureOptions();
        configure?.Invoke(options);
        return builder.AddRuntimeFeature(new ConnectivityRuntimeFeature(options.Validated()));
    }
}

internal sealed class ConnectivityRuntimeFeature(ConnectivityCaptureOptions options) : IRuntimeFeature, INativeRuntimeOptionsContributor
{
    public string Id => "connectivity";

    public void ContributeNativeOptions(JsonObject nativeOptions)
        => nativeOptions["connectivityCapture"] = new JsonObject
        {
            ["minimumSampleIntervalMilliseconds"] = options.MinimumSampleIntervalMilliseconds
        };

    public void Initialize(IRuntime runtime)
    {
#if !ANDROID && !IOS && !MACCATALYST
        var monitor = new DesktopConnectivityMonitor(runtime);
        runtime.OnActivated += (_, _) => monitor.Start();
        runtime.OnDeactivated += (_, _) => monitor.Stop();
        if (runtime.IsActive) monitor.Start();
#endif
    }
}
