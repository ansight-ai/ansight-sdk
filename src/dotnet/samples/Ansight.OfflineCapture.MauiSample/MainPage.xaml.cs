using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Ansight.OfflineCapture.MauiSample;

public partial class MainPage : ContentPage
{
    private readonly OfflineCaptureHarness captureHarness = OfflineCaptureHarness.Shared;
    private bool initialized;
    private bool autorunCompleted;
    private bool busy;
    private int touchCount;

    public MainPage()
    {
        InitializeComponent();
        ApplyOptionLabels();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!initialized)
        {
            await RunAsync(async () =>
            {
                await captureHarness.InitializeAsync();
                initialized = true;
            });
        }

        await RefreshAsync();

        if (!autorunCompleted && ShouldAutorun())
        {
            autorunCompleted = true;
            await RunAsync(RunAutorunScenarioAsync);
        }
    }

    private async void HandleInitializeClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            await captureHarness.InitializeAsync();
            initialized = true;
        });
    }

    private async void HandleStartClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () => await captureHarness.StartAsync());
    }

    private async void HandleStopClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () => await captureHarness.StopAsync());
    }

    private async void HandleNextSessionClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () => await captureHarness.SetActivationModeAsync(OfflineCaptureActivationMode.NextSessionOnly));
    }

    private async void HandleAlwaysOnClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () => await captureHarness.SetActivationModeAsync(OfflineCaptureActivationMode.AlwaysOn));
    }

    private async void HandleDisableActivationClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () => await captureHarness.SetActivationModeAsync(OfflineCaptureActivationMode.Disabled));
    }

    private async void HandleApplyOptionsClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            await captureHarness.ApplyOptionsAsync(
                (int)RetentionSlider.Value,
                (int)MaximumSessionSlider.Value,
                (int)SegmentSlider.Value,
                UseJpegOverrideSwitch.IsToggled,
                JpegEnabledSwitch.IsToggled,
                (int)JpegIntervalSlider.Value,
                (int)JpegQualitySlider.Value,
                (int)JpegMaxWidthSlider.Value);
        });
    }

    private async void HandleMetricClicked(object? sender, EventArgs e)
    {
        captureHarness.RecordMetric();
        await RefreshAsync();
    }

    private async void HandleEventClicked(object? sender, EventArgs e)
    {
        captureHarness.RecordEvent($"Manual event {DateTimeOffset.Now:T}");
        await RefreshAsync();
    }

    private async void HandleBurstClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () => await captureHarness.GenerateBurstAsync(40));
    }

    private async void HandleScreenViewClicked(object? sender, EventArgs e)
    {
        captureHarness.RecordScreenView("Offline Capture Sample");
        await RefreshAsync();
    }

    private async void HandleCustomPropertyClicked(object? sender, EventArgs e)
    {
        captureHarness.RegisterSessionProperty();
        captureHarness.RecordEvent("Updated custom session property");
        await RefreshAsync();
    }

    private async void HandleClearPropertiesClicked(object? sender, EventArgs e)
    {
        captureHarness.ClearSessionProperties();
        captureHarness.RecordEvent("Cleared custom session properties");
        await RefreshAsync();
    }

    private async void HandleForegroundClicked(object? sender, EventArgs e)
    {
        Runtime.SetAppLifecycleState(AppLifecycleState.Foreground);
        await RefreshAsync();
    }

    private async void HandleBackgroundClicked(object? sender, EventArgs e)
    {
        Runtime.SetAppLifecycleState(AppLifecycleState.Background);
        await RefreshAsync();
    }

    private async void HandleAddMemoryClicked(object? sender, EventArgs e)
    {
        captureHarness.AddMemoryPressure(16);
        await RefreshAsync();
    }

    private async void HandleReleaseMemoryClicked(object? sender, EventArgs e)
    {
        captureHarness.ReleaseMemoryPressure();
        await RefreshAsync();
    }

    private async void HandleTouchPadTapped(object? sender, TappedEventArgs e)
    {
        touchCount++;
        captureHarness.RecordInteraction($"Touch pad tap {touchCount}");
        TouchStatusLabel.Text = $"Tap count {touchCount}";
        await RefreshAsync();
    }

    private async void HandleTouchPadPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType != GestureStatus.Completed)
        {
            return;
        }

        touchCount++;
        captureHarness.RecordInteraction($"Touch pad pan {touchCount}");
        TouchStatusLabel.Text = $"Pan count {touchCount}";
        await RefreshAsync();
    }

    private async void HandleExportClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            var path = await captureHarness.ExportAsync(PasswordEntry.Text);
            await DisplayAlert("Export complete", path, "OK");
        });
    }

    private async void HandleShareClicked(object? sender, EventArgs e)
    {
        var exportPath = captureHarness.LastExportPath;
        if (string.IsNullOrWhiteSpace(exportPath) || !File.Exists(exportPath))
        {
            await DisplayAlert("Share", "Export a ZIP first.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Ansight offline capture",
            File = new ShareFile(exportPath)
        });
    }

    private async void HandleRefreshClicked(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private void HandleOptionSliderChanged(object? sender, ValueChangedEventArgs e)
    {
        ApplyOptionLabels();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        try
        {
            CaptureStatusLabel.Text = "Working";
            await action();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            CaptureStatusLabel.Text = "Error";
            await DisplayAlert("Offline capture", ex.Message, "OK");
        }
        finally
        {
            busy = false;
        }
    }

    private async Task RunAutorunScenarioAsync()
    {
        await captureHarness.ApplyOptionsAsync(
            retentionSeconds: 30,
            maximumSessionMegabytes: 64,
            segmentSeconds: 5,
            useJpegOverride: true,
            jpegEnabled: true,
            jpegIntervalMilliseconds: 750,
            jpegQuality: 55,
            jpegMaxWidth: 540);
        await captureHarness.StartAsync();
        var connectionResult = await Runtime.HostConnection.ConnectAsync(
            HostConnectionRequest.SavedConfig("simulator autorun"),
            "Ansight Offline Capture");
        captureHarness.RecordEvent(connectionResult.Success
            ? "Connected to Ansight host"
            : $"Ansight host connection failed: {connectionResult.Message}");
        captureHarness.RegisterSessionProperty();
        captureHarness.RecordScreenView("Offline Capture Autorun");
        captureHarness.RecordInteraction("Autorun touch marker");
        captureHarness.AddMemoryPressure(16);
        await captureHarness.GenerateBurstAsync(40);
        await Task.Delay(1600);
        captureHarness.ReleaseMemoryPressure();
        await captureHarness.ExportAsync(password: null);
    }

    private static bool ShouldAutorun()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("ANSIGHT_OFFLINE_CAPTURE_AUTORUN"), "1", StringComparison.Ordinal))
        {
            return true;
        }

        var arguments = Environment.GetCommandLineArgs();
        return arguments.Any(argument => string.Equals(argument, "--ansight-offline-capture-autorun", StringComparison.Ordinal));
    }

    private async Task RefreshAsync()
    {
        var summary = await captureHarness.GetSummaryAsync();
        var options = captureHarness.Options;
        CaptureStatusLabel.Text = summary.IsCapturing
            ? $"Capturing  |  activation {options.ActivationMode}"
            : $"Stopped  |  activation {options.ActivationMode}";
        SessionStatusLabel.Text = summary.Session is null
            ? $"Runtime active {Runtime.IsActive}  |  retained buffers {summary.RetainedBufferCount}"
            : $"Session {summary.Session.SessionId}  |  {FormatBytes(summary.TotalBytes)}  |  active {summary.Session.IsActive}";
        CaptureRootLabel.Text = summary.CaptureRoot;
        RecordCountsLabel.Text =
            $"metrics {summary.MetricSegmentCount}  events {summary.EventSegmentCount}  touches {summary.TouchSegmentCount}  screenshots {summary.ScreenshotCount}  indexes {summary.ScreenshotIndexSegmentCount}";
        LastExportLabel.Text = string.IsNullOrWhiteSpace(summary.LastExportPath)
            ? "No ZIP export yet"
            : summary.LastExportPath;
    }

    private void ApplyOptionLabels()
    {
        RetentionValueLabel.Text = $"Retention override: {(int)RetentionSlider.Value}s";
        MaximumSessionValueLabel.Text = $"Maximum session size: {(int)MaximumSessionSlider.Value} MB";
        SegmentValueLabel.Text = $"Segment duration: {(int)SegmentSlider.Value}s";
        JpegIntervalValueLabel.Text = $"JPEG interval: {(int)JpegIntervalSlider.Value} ms";
        JpegQualityValueLabel.Text = $"JPEG quality: {(int)JpegQualitySlider.Value}";
        JpegMaxWidthValueLabel.Text = $"JPEG max width: {(int)JpegMaxWidthSlider.Value}px";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }
}
