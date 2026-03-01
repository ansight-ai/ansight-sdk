using System.Text.Json;
using System.Threading;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using SkiaSharp;

namespace Ansight.TestHarness;

public partial class MainPage : ContentPage
{
    private readonly List<byte[]> spikes = new();
    private readonly List<object> nativeAllocations = new();
    private readonly List<SKBitmap> leakedSkiaBitmaps = new();
    private readonly List<SKImage> leakedSkiaImages = new();
    private readonly Stack<Action> releaseActions = new();
    private readonly object spikeLock = new();
    private readonly Random random = new();
    private CancellationTokenSource? frameDropCts;

    public MainPage()
    {
        InitializeComponent();
        ShakePredicateCodeOneEntry.Text = ShakePredicateCoordinator.CodeOne;
        ShakePredicateCodeTwoEntry.Text = ShakePredicateCoordinator.CodeTwo;
        UpdateRuntimeStatus();
        UpdateShakePredicateStatus();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateRuntimeStatus();
        UpdateShakePredicateStatus();
    }

    private void UpdateRuntimeStatus()
    {
        var isActive = Runtime.IsActive;
        var isPresented = Runtime.IsSheetPresented;
        StatusLabel.Text = $"Runtime {(isActive ? "active" : "inactive")} • {(isPresented ? "presented" : "hidden")}";
        UpdateEventRenderingStatus();
        UpdateChartThemeStatus();
    }

    private void UpdateShakePredicateStatus()
    {
        if (ShakePredicateStatusLabel == null)
        {
            return;
        }

        var allowed = ShakePredicateCoordinator.ShouldAllowShake;
        ShakePredicateStatusLabel.Text = allowed
            ? "Current: Allowed (tokens match)"
            : "Current: Blocked (tokens mismatch)";
    }

    private void UpdateEventRenderingStatus()
    {
        EventRenderingStatusLabel.Text = $"Current: {Runtime.AppEventRenderingBehaviour}";
    }

    private void UpdateChartThemeStatus()
    {
        ChartThemeStatusLabel.Text = $"Current: {Runtime.ChartTheme}";
    }

    private void OnActivateClicked(object? sender, EventArgs e)
    {
        Runtime.Activate();
        UpdateRuntimeStatus();
    }

    private void OnLowFrameDropClicked(object? sender, EventArgs e) => StartFrameDrop(TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(50));
    private void OnMediumFrameDropClicked(object? sender, EventArgs e) => StartFrameDrop(TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(150));
    private void OnHeavyFrameDropClicked(object? sender, EventArgs e) => StartFrameDrop(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(350));
    private void OnExtremeFrameDropClicked(object? sender, EventArgs e) => StartFrameDrop(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(600));
    private void OnStopFrameDropClicked(object? sender, EventArgs e) => StopFrameDrop();

    private void OnDeactivateClicked(object? sender, EventArgs e)
    {
        Runtime.Deactivate();
        UpdateRuntimeStatus();
    }

    private void OnTriggerGcClicked(object? sender, EventArgs e)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Runtime.Event("Manual GC", CustomAnsightConfiguration.CustomEventChannelId);
        UpdateRuntimeStatus();
    }

    private void OnPresentSheetClicked(object? sender, EventArgs e)
    {
        Runtime.PresentSheet();
        UpdateRuntimeStatus();
    }

    private void OnEnableFpsClicked(object? sender, EventArgs e)
    {
        Runtime.EnableFramesPerSecond();
        UpdateRuntimeStatus();
    }

    private void OnDisableFpsClicked(object? sender, EventArgs e)
    {
        Runtime.DisableFramesPerSecond();
        UpdateRuntimeStatus();
    }
    
    private void OnEventLabelsAndIconsClicked(object? sender, EventArgs e) => SetEventRenderingBehaviour(AppEventRenderingBehaviour.LabelsAndIcons);

    private void OnEventIconsOnlyClicked(object? sender, EventArgs e) => SetEventRenderingBehaviour(AppEventRenderingBehaviour.IconsOnly);

    private void OnEventHiddenClicked(object? sender, EventArgs e) => SetEventRenderingBehaviour(AppEventRenderingBehaviour.None);

    private void SetEventRenderingBehaviour(AppEventRenderingBehaviour behaviour)
    {
        Runtime.AppEventRenderingBehaviour = behaviour;
        UpdateEventRenderingStatus();
    }

    private void OnLightThemeClicked(object? sender, EventArgs e)
    {
        Runtime.ChartTheme = ChartTheme.Light;
        UpdateChartThemeStatus();
    }

    private void OnDarkThemeClicked(object? sender, EventArgs e)
    {
        Runtime.ChartTheme = ChartTheme.Dark;
        UpdateChartThemeStatus();
    }


    private void StartFrameDrop(TimeSpan interval, TimeSpan blockDuration)
    {
        StopFrameDrop();
        frameDropCts = new CancellationTokenSource();
        var token = frameDropCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        // Intentional UI thread stall to simulate jank/fps drops.
                        Thread.Sleep(blockDuration);
                    });

                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // swallow to keep the loop alive for test purposes
                }
            }
        }, token);
    }

    private void StopFrameDrop()
    {
        try
        {
            frameDropCts?.Cancel();
            frameDropCts?.Dispose();
        }
        finally
        {
            frameDropCts = null;
        }
    }

    private void OnPresentChartOverlayClicked(object? sender, EventArgs e)
    {
        if (Runtime.IsChartOverlayPresented)
        {
            Runtime.DismissOverlay();
        }
        else
        {
            Runtime.PresentOverlay(OverlayPosition.TopRight);
        }

        UpdateRuntimeStatus();
    }

    private void OnDismissClicked(object? sender, EventArgs e)
    {
#if ANDROID || IOS
        Runtime.DismissOverlay();
#endif
        
        Runtime.DismissSheet();
        UpdateRuntimeStatus();
    }

    private void PresentOverlay(OverlayPosition position)
    {
        Runtime.PresentOverlay(position);
        UpdateRuntimeStatus();
    }

    private void OnOverlayTopLeftClicked(object? sender, EventArgs e) => PresentOverlay(OverlayPosition.TopLeft);

    private void OnOverlayTopRightClicked(object? sender, EventArgs e) => PresentOverlay(OverlayPosition.TopRight);

    private void OnOverlayBottomLeftClicked(object? sender, EventArgs e) => PresentOverlay(OverlayPosition.BottomLeft);

    private void OnOverlayBottomRightClicked(object? sender, EventArgs e) => PresentOverlay(OverlayPosition.BottomRight);

    private void OnShakePredicateCodeOneChanged(object? sender, TextChangedEventArgs e)
    {
        ShakePredicateCoordinator.UpdateCodeOne(e.NewTextValue);
        UpdateShakePredicateStatus();
    }

    private void OnShakePredicateCodeTwoChanged(object? sender, TextChangedEventArgs e)
    {
        ShakePredicateCoordinator.UpdateCodeTwo(e.NewTextValue);
        UpdateShakePredicateStatus();
    }


    private void OnLowSpikeClicked(object? sender, EventArgs e) => RunClrMemorySpike("Low", 4).SafeFireAndForget();

    private void OnMediumSpikeClicked(object? sender, EventArgs e) => RunClrMemorySpike("Medium", 16).SafeFireAndForget();

    private void OnHighSpikeClicked(object? sender, EventArgs e) => RunClrMemorySpike("High", 48).SafeFireAndForget();

    private  void OnExtremeSpikeClicked(object? sender, EventArgs e) => RunClrMemorySpike("Extreme", 96).SafeFireAndForget();

    private  void OnLowNativeClicked(object? sender, EventArgs e) => RunNativeSpike("Native Low", 500).SafeFireAndForget();

    private  void OnMediumNativeClicked(object? sender, EventArgs e) => RunNativeSpike("Native Medium", 2000).SafeFireAndForget();

    private  void OnHighNativeClicked(object? sender, EventArgs e) => RunNativeSpike("Native High", 4000).SafeFireAndForget();

    private  void OnExtremeNativeClicked(object? sender, EventArgs e) => RunNativeSpike("Native Extreme", 12000).SafeFireAndForget();

    private void OnLeakImageClicked(object? sender, EventArgs e)
    {
        // simulate a retained native-backed image leak using Skia
        const int width = 4096;
        const int height = 4096; // ~67 MB for RGBA

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor((uint)random.Next()));
        }

        var skImage = SKImage.FromBitmap(bitmap);

        leakedSkiaBitmaps.Add(bitmap);
        leakedSkiaImages.Add(skImage);

        Runtime.Event("Leaked large Skia image (~67 MB)", CustomAnsightConfiguration.CustomEventChannelId);
        UpdateRuntimeStatus();
    }

    private Task RunClrMemorySpike(string label, int sizeMb)
    {
        try
        {
            var buffer = new byte[sizeMb * 1024 * 1024];
            lock (spikeLock)
            {
                spikes.Add(buffer);
            }

            Runtime.Event($"Memory spike ({label})", CustomAnsightConfiguration.CustomEventChannelId);

            releaseActions.Push(() =>
            {
                lock (spikeLock)
                {
                    spikes.Remove(buffer);
                }
            });
        }
        catch (OutOfMemoryException)
        {
            Runtime.Event($"Failed spike ({label})", Constants.ReservedChannels.ChannelNotSpecified_Id);
        }
        finally
        {
            UpdateRuntimeStatus();
        }

        return Task.CompletedTask;
    }

    private async Task RunNativeSpike(string label, int count)
    {
        try
        {
#if ANDROID
            var list = new List<Java.Lang.Object>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new Java.Lang.String($"ansight-{i}-{DateTime.UtcNow.Ticks}"));
            }
            Runtime.Event($"{label} (Java objects)", CustomAnsightConfiguration.CustomEventChannelId);
            nativeAllocations.Add(list);
            releaseActions.Push(() =>
            {
                list.Clear();
                nativeAllocations.Remove(list);
            });
#elif IOS || MACCATALYST
            var list = new List<Foundation.NSObject>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new Foundation.NSString($"ansight-{i}-{DateTime.UtcNow.Ticks}"));
            }
            Runtime.Event($"{label} (NSObjects)", CustomAnsightConfiguration.CustomEventChannelId);
            nativeAllocations.Add(list);
            releaseActions.Push(() =>
            {
                list.Clear();
                nativeAllocations.Remove(list);
            });
#else
            await Task.CompletedTask;
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        finally
        {
            UpdateRuntimeStatus();
        }
    }

    private void OnReleaseAllocationClicked(object? sender, EventArgs e)
    {
        if (releaseActions.Count == 0)
        {
            return;
        }

        var action = releaseActions.Pop();
        action();

        Runtime.Event("Released allocation", CustomAnsightConfiguration.CustomEventChannelId);
        UpdateRuntimeStatus();
    }

    private void OnRecordCustomMetricClicked(object? sender, EventArgs e)
    {
        var value = random.Next(8, 200) * 1024L * 1024L;
        Runtime.Metric(value, CustomAnsightConfiguration.CustomMetricChannelId);
        UpdateRuntimeStatus();
    }

    private void OnRecordCustomEventClicked(object? sender, EventArgs e)
    {
        Runtime.Event($"Custom event @ {DateTime.Now:HH:mm:ss}", CustomAnsightConfiguration.CustomEventChannelId);
        UpdateRuntimeStatus();
    }

    private void OnRecordDetachedEventClicked(object? sender, EventArgs e)
    {
        Runtime.Event("Detached channel event", Constants.ReservedChannels.ChannelNotSpecified_Id);
        UpdateRuntimeStatus();
    }

    private void OnClearDataClicked(object? sender, EventArgs e)
    {
        Runtime.Clear();
        UpdateRuntimeStatus();
    }

    private void OnCloseOverlayClicked(object? sender, EventArgs e)
    {
        UpdateRuntimeStatus();
    }

    private async void OnPushNavigationPageClicked(object? sender, EventArgs e)
    {
        if (Navigation == null)
        {
            return;
        }

        Runtime.Event("Push NavigationTestPage", CustomAnsightConfiguration.CustomEventChannelId);
        await Navigation.PushAsync(new NavigationTestPage());
    }

    private async void OnPushModalPageClicked(object? sender, EventArgs e)
    {
        if (Navigation == null)
        {
            return;
        }

        Runtime.Event("Push ModalTestPage", CustomAnsightConfiguration.CustomEventChannelId);
        await Navigation.PushModalAsync(new ModalTestPage());
    }

    private async void OnPushLeakyPageClicked(object? sender, EventArgs e)
    {
        if (Navigation == null)
        {
            return;
        }

        Runtime.Event("Push ImagePage", CustomAnsightConfiguration.CustomEventChannelId);
        await Navigation.PushAsync(new ImagePage());
    }

    private async void OnExportSnapshotClicked(object? sender, EventArgs e)
    {
        if (!Runtime.IsInitialized)
        {
            await DisplayAlert("Snapshot", "Ansight runtime is not initialized.", "OK");
            return;
        }

        var snapshot = Runtime.Instance.DataSink.Snapshot();
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await Clipboard.Default.SetTextAsync(json);
        await DisplayAlert("Snapshot", "Snapshot copied to clipboard as JSON.", "OK");
    }
}
