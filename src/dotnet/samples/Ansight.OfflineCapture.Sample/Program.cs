using Ansight;
using Ansight.OfflineCapture;

var captureRoot = Path.Combine(AppContext.BaseDirectory, ".ansight");
var exportPath = Path.Combine(AppContext.BaseDirectory, "ansight-offline-capture.zip");
var password = args.FirstOrDefault(arg => arg.StartsWith("--password=", StringComparison.Ordinal))
    ?.Split('=', 2)[1];

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithoutSessionJpegCapture();
    })
    .Build();

Runtime.InitializeAndActivate(options);

var offlineCapture = OfflineCapture.Configure(new OfflineCaptureOptions
{
    RootDirectory = captureRoot,
    MaximumSessionBytes = 32 * 1024 * 1024,
    MaximumRetainedBytes = 64 * 1024 * 1024
});

await offlineCapture.InitializeAsync();
await offlineCapture.StartAsync();

Runtime.Event("offline_capture_sample_started");
for (var i = 0; i < 5; i++)
{
    Runtime.Metric(60 + i, Constants.ReservedChannels.FramesPerSecond_Id);
    await Task.Delay(100);
}

await offlineCapture.UpdateOptionsAsync(capture =>
{
    capture.RetentionWindowOverride = TimeSpan.FromSeconds(15);
    capture.MaximumSessionBytes = 16 * 1024 * 1024;
});

Runtime.Event("offline_capture_sample_exporting");
await offlineCapture.ExportToFileAsync(exportPath, new OfflineCaptureExportOptions
{
    Password = password
});
await offlineCapture.StopAsync();

Console.WriteLine(exportPath);
