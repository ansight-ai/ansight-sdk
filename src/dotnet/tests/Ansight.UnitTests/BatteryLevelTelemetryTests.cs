using Ansight.Telemetry.Battery;

namespace Ansight.UnitTests;

public sealed class BatteryLevelTelemetryTests
{
    [Fact]
    public void Options_DisablesBatteryLevelByDefault()
    {
        var options = Options.CreateBuilder().Build();

        Assert.False(options.EnableBatteryLevel);
    }

    [Fact]
    public void Options_CanEnableAndDisableBatteryLevel()
    {
        var enabledOptions = Options.CreateBuilder()
            .WithBatteryLevel()
            .Build();

        var disabledOptions = Options.CreateBuilder(enabledOptions)
            .WithoutBatteryLevel()
            .Build();

        Assert.True(enabledOptions.EnableBatteryLevel);
        Assert.False(disabledOptions.EnableBatteryLevel);
    }

    [Fact]
    public void Options_WithAnsightDefaultsKeepsBatteryLevelDisabled()
    {
        var options = Options.CreateBuilder()
            .WithAnsightDefaults()
            .Build();

        Assert.False(options.EnableBatteryLevel);
    }

    [Fact]
    public void MutableDataSink_DoesNotIncludeBatteryChannelWhenDisabled()
    {
        BatteryLevelMonitorRegistry.RegisterFactory(() => new FakeBatteryLevelMonitor(86), supported: true);
        try
        {
            var options = Options.CreateBuilder().Build();
            var dataSink = new MutableDataSink(options);

            Assert.DoesNotContain(dataSink.Channels, channel => channel.Id == Constants.ReservedChannels.BatteryLevel_Id);
        }
        finally
        {
            BatteryLevelMonitorRegistry.Reset();
        }
    }

    [Fact]
    public void MutableDataSink_DoesNotIncludeBatteryChannelWhenPlatformUnsupported()
    {
        BatteryLevelMonitorRegistry.RegisterFactory(() => new FakeBatteryLevelMonitor(86, supported: false), supported: false);
        try
        {
            var options = Options.CreateBuilder()
                .WithBatteryLevel()
                .Build();
            var dataSink = new MutableDataSink(options);

            Assert.DoesNotContain(dataSink.Channels, channel => channel.Id == Constants.ReservedChannels.BatteryLevel_Id);
        }
        finally
        {
            BatteryLevelMonitorRegistry.Reset();
        }
    }

    [Fact]
    public void MutableDataSink_RecordsBatteryLevelWhenEnabledAndSupported()
    {
        BatteryLevelMonitorRegistry.RegisterFactory(() => new FakeBatteryLevelMonitor(86), supported: true);
        try
        {
            var options = Options.CreateBuilder()
                .WithBatteryLevel()
                .Build();
            var dataSink = new MutableDataSink(options);

            dataSink.RecordBatteryLevel(86);

            var batteryChannel = Assert.Single(dataSink.Channels, channel => channel.Id == Constants.ReservedChannels.BatteryLevel_Id);
            var metric = Assert.Single(dataSink.GetMetricsForChannel(Constants.ReservedChannels.BatteryLevel_Id));
            Assert.Equal(Constants.ReservedChannels.BatteryLevel_Name, batteryChannel.Name);
            Assert.Equal(86, metric.Value);
        }
        finally
        {
            BatteryLevelMonitorRegistry.Reset();
        }
    }

    [Fact]
    public void MutableDataSink_ClampsBatteryLevelToPercentageRange()
    {
        BatteryLevelMonitorRegistry.RegisterFactory(() => new FakeBatteryLevelMonitor(125), supported: true);
        try
        {
            var options = Options.CreateBuilder()
                .WithBatteryLevel()
                .Build();
            var dataSink = new MutableDataSink(options);

            dataSink.RecordBatteryLevel(125);

            var metric = Assert.Single(dataSink.GetMetricsForChannel(Constants.ReservedChannels.BatteryLevel_Id));
            Assert.Equal(100, metric.Value);
        }
        finally
        {
            BatteryLevelMonitorRegistry.Reset();
        }
    }

    [Fact]
    public async Task RuntimeImpl_RecordsBatteryLevelSamplesWhenEnabledAndSupported()
    {
        var monitor = new FakeBatteryLevelMonitor(72);
        BatteryLevelMonitorRegistry.RegisterFactory(() => monitor, supported: true);
        try
        {
            var options = Options.CreateBuilder()
                .WithBatteryLevel()
                .WithoutHostAutoProbe()
                .WithSampleFrequencyMilliseconds(Constants.MinSampleFrequencyMilliseconds)
                .Build();
            var runtime = new RuntimeImpl(options);
            var sampleReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.DataSink.OnMetricsUpdated += (_, args) =>
            {
                if (args.Added.Any(metric => metric.Channel == Constants.ReservedChannels.BatteryLevel_Id))
                {
                    sampleReceived.TrySetResult();
                }
            };

            runtime.Activate();
            try
            {
                await sampleReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var metric = Assert.Single(runtime.DataSink.GetMetricsForChannel(Constants.ReservedChannels.BatteryLevel_Id));

                Assert.True(monitor.Started);
                Assert.Equal(72, metric.Value);
            }
            finally
            {
                runtime.Deactivate();
            }

            Assert.True(monitor.Stopped);
        }
        finally
        {
            BatteryLevelMonitorRegistry.Reset();
        }
    }

    [Fact]
    public void Constants_RejectsBatteryLevelAsAdditionalChannel()
    {
        var optionsBuilder = Options.CreateBuilder()
            .AddAdditionalChannel(new Channel(
                Constants.ReservedChannels.BatteryLevel_Id,
                "Duplicate Battery",
                default));

        Assert.Throws<InvalidOperationException>(() => optionsBuilder.Build());
    }

    private sealed class FakeBatteryLevelMonitor : IBatteryLevelMonitor
    {
        private readonly long? batteryLevelPercentage;

        public FakeBatteryLevelMonitor(long? batteryLevelPercentage, bool supported = true)
        {
            this.batteryLevelPercentage = batteryLevelPercentage;
            IsSupported = supported;
        }

        public bool IsSupported { get; }

        public bool Started { get; private set; }

        public bool Stopped { get; private set; }

        public void Start()
        {
            Started = true;
        }

        public void Stop()
        {
            Stopped = true;
        }

        public long? ReadBatteryLevelPercentage() => batteryLevelPercentage;

        public void Dispose()
        {
        }
    }
}
