#if IOS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreGraphics;
using Foundation;
using SkiaSharp.Views.iOS;
using UIKit;
using System.Collections.Generic;
using SkiaSharp;

namespace Ansight;

internal sealed class IosNativePresentationService : IPresentationService
{
    private readonly Options options;
    private readonly IDataSink dataSink;
    private readonly Func<UIWindow?> windowProvider;
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private UIViewController? sheetController;
    private UIView? overlayView;
    private NativeChartViewIos? overlayChart;

    public IosNativePresentationService(Options options, IDataSink dataSink, Func<UIWindow?> windowProvider)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.dataSink = dataSink ?? throw new ArgumentNullException(nameof(dataSink));
        this.windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    public bool IsPresentationEnabled => true;
    public bool IsSheetPresented => sheetController?.PresentedViewController != null || (sheetController?.View?.Window != null);
    public bool IsOverlayPresented => overlayView?.Hidden == false;

    public void PresentSheet()
    {
        var window = windowProvider();
        var root = window?.RootViewController;
        if (root == null)
        {
            Logger.Error("PresentSheet failed: no UIWindow available.");
            return;
        }

        UIApplication.SharedApplication.InvokeOnMainThread(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                DismissSheetInternal();

                sheetController = new UIViewController();
                sheetController.View = new SheetView(window.Bounds, dataSink, options);
                sheetController.ModalPresentationStyle = UIModalPresentationStyle.PageSheet;

                root.PresentViewController(sheetController, true, null);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    public void DismissSheet()
    {
        UIApplication.SharedApplication.InvokeOnMainThread(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                DismissSheetInternal();
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    public void PresentOverlay(OverlayPosition position)
    {
        var window = windowProvider();
        if (window == null)
        {
            Logger.Error("PresentOverlay failed: no UIWindow available.");
            return;
        }

        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            EnsureOverlay(window);

            PositionOverlay(window, position);
            overlayView.Hidden = false;
        });
    }

    public void DismissOverlay()
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (overlayView != null)
            {
                overlayView.Hidden = true;
            }
        });
    }

    private void EnsureOverlay(UIWindow window)
    {
        if (overlayView == null)
        {
            overlayView = new UIView(window.Bounds)
            {
                BackgroundColor = UIColor.Clear,
                UserInteractionEnabled = false,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
            };

            window.AddSubview(overlayView);
        }

        if (overlayChart == null || overlayChart.DataSink == null || overlayChart.Superview == null)
        {
            overlayChart = new NativeChartViewIos(new CGRect(0, 0, 220, 130))
            {
                DataSink = dataSink,
                RenderMode = ChartRenderMode.Overlay,
                WindowDuration = TimeSpan.FromMinutes(1),
                UserInteractionEnabled = false,
                BackgroundColor = UIColor.Clear,
                Opaque = false
            };
            overlayChart.Layer.CornerRadius = 12;
            overlayChart.Layer.MasksToBounds = true;

            if (overlayChart.GestureRecognizers != null)
            {
                foreach (var recognizer in overlayChart.GestureRecognizers.ToArray())
                {
                    overlayChart.RemoveGestureRecognizer(recognizer);
                }
            }

            overlayView.Subviews?.FirstOrDefault()?.RemoveFromSuperview();
            overlayView.AddSubview(overlayChart);
        }
    }
    

    private void PositionOverlay(UIWindow window, OverlayPosition position)
    {
        if (overlayView == null || overlayChart == null)
        {
            return;
        }

        var margin = 12;
        var frame = overlayChart.Frame;
        switch (position)
        {
            case OverlayPosition.TopLeft:
                frame.X = margin;
                frame.Y = margin;
                break;
            case OverlayPosition.TopRight:
                frame.X = window.Bounds.Width - frame.Width - margin;
                frame.Y = margin;
                break;
            case OverlayPosition.BottomLeft:
                frame.X = margin;
                frame.Y = window.Bounds.Height - frame.Height - margin;
                break;
            default:
                frame.X = window.Bounds.Width - frame.Width - margin;
                frame.Y = window.Bounds.Height - frame.Height - margin;
                break;
        }

        overlayChart.Frame = frame;
    }

    private void DismissSheetInternal()
    {
        if (sheetController == null)
        {
            return;
        }

        try
        {
            sheetController.DismissViewController(true, null);
        }
        catch { }
        finally
        {
            sheetController = null;
        }
    }

    public void Dispose()
    {
        DismissSheetInternal();
        overlayChart?.Detach();
        overlayView?.RemoveFromSuperview();
        overlayView = null;
    }
}

internal sealed class NativeChartViewIos : SKCanvasView
{
    private IDataSink? dataSink;
    private NSTimer? timer;
    private float? probeRatio;
    private SKRect? chartBounds;
    private float lastCanvasWidth;
    private float lastCanvasHeight;

    public NativeChartViewIos(CGRect frame) : base(frame)
    {
        IgnorePixelScaling = true;
        PaintSurface += OnPaintSurface;
        var tapRecognizer = new UITapGestureRecognizer(OnTapped);
        AddGestureRecognizer(tapRecognizer);
        var panRecognizer = new UIPanGestureRecognizer(OnPan);
        AddGestureRecognizer(panRecognizer);
        timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(500), _ => RequestRedraw());
    }

    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(1);
    public ChartRenderMode RenderMode { get; set; } = ChartRenderMode.Inline;

    public IDataSink? DataSink
    {
        get => dataSink;
        set
        {
            if (dataSink != null)
            {
                dataSink.OnMetricsUpdated -= HandleMetricsUpdated;
                dataSink.OnEventsUpdated -= HandleEventsUpdated;
            }

            dataSink = value;

            if (dataSink != null)
            {
                dataSink.OnMetricsUpdated += HandleMetricsUpdated;
                dataSink.OnEventsUpdated += HandleEventsUpdated;
            }

            RequestRedraw();
        }
    }

    private void HandleMetricsUpdated(object? sender, MetricsUpdatedEventArgs e) => RequestRedraw();
    private void HandleEventsUpdated(object? sender, AppEventsUpdatedEventArgs e) => RequestRedraw();

    private void RequestRedraw()
    {
        if (NSThread.IsMain)
        {
            SetNeedsDisplay();
            return;
        }

        UIApplication.SharedApplication.InvokeOnMainThread(SetNeedsDisplay);
    }

    private void OnPaintSurface(object? sender, SkiaSharp.Views.iOS.SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        lastCanvasWidth = e.Info.Width;
        lastCanvasHeight = e.Info.Height;
        var sink = dataSink;
        if (sink == null)
        {
            canvas.Clear();
            return;
        }

        var channels = sink.Channels?.Select(c => c.Id).ToArray() ?? Array.Empty<byte>();
        var now = DateTime.UtcNow;
        if (WindowDuration <= TimeSpan.Zero)
        {
            WindowDuration = TimeSpan.FromSeconds(30);
        }

        var renderOptions = new RenderOptions()
        {
            Channels = channels,
            FromUtc = now - WindowDuration,
            ToUtc = now,
            CurrentUtc = now,
            Mode = RenderMode,
            ProbePosition = probeRatio,
            AppEventRenderingBehaviour = Runtime.AppEventRenderingBehaviour,
            Theme = Runtime.ChartTheme
        };

        var renderResult = ChartRenderer.Render(canvas, e.Info, sink, renderOptions);

        if (renderResult.HasChartArea && renderResult.ChartBounds.Width > 0 && renderResult.ChartBounds.Height > 0)
        {
            chartBounds = renderResult.ChartBounds;
        }
        else
        {
            chartBounds = null;
        }
    }

    private void OnTapped()
    {
        if (RenderMode == ChartRenderMode.Inline)
        {
            probeRatio = null;
            SetNeedsDisplay();
        }
    }

    private void OnPan(UIPanGestureRecognizer recognizer)
    {
        if (RenderMode == ChartRenderMode.Overlay)
        {
            probeRatio = null;
            return;
        }

        if (recognizer.State == UIGestureRecognizerState.Ended
            || recognizer.State == UIGestureRecognizerState.Cancelled)
        {
            probeRatio = null;
            SetNeedsDisplay();
            return;
        }

        var location = recognizer.LocationInView(this);
        var chartRect = chartBounds;
        if (!chartRect.HasValue || chartRect.Value.Width <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        var scaleX = lastCanvasWidth > 0 ? lastCanvasWidth / (float)Bounds.Width : 1f;
        var touchX = (float)location.X * scaleX;
        var relativeX = (touchX - chartRect.Value.Left) / chartRect.Value.Width;
        var ratio = Math.Clamp(relativeX, 0f, 1f);
        if (!float.IsNaN(ratio))
        {
            probeRatio = ratio;
            SetNeedsDisplay();
        }
    }

    public void Detach()
    {
        if (timer != null)
        {
            timer.Invalidate();
            timer.Dispose();
            timer = null;
        }

        if (dataSink != null)
        {
            dataSink.OnMetricsUpdated -= HandleMetricsUpdated;
            dataSink.OnEventsUpdated -= HandleEventsUpdated;
            dataSink = null;
        }

        PaintSurface -= OnPaintSurface;
    }
}

internal sealed class SheetView : UIView
{
    private readonly UILabel titleLabel;
    private readonly UIButton overlayButton;
    private readonly UIButton? copyButton;
    private readonly NativeChartViewIos chart;
    private readonly UITableView table;
    private readonly EventsTableSource eventsSource;

    private static readonly nfloat ChartHeight = new nfloat(220);
    private static readonly nfloat HorizontalPadding = new nfloat(12);
    private static readonly nfloat VerticalPadding = new nfloat(12);
    private static readonly nfloat SectionSpacing = new nfloat(8);
    private static readonly nfloat ButtonHeight = new nfloat(34);

    internal SheetView(CGRect frame, IDataSink dataSink, Options options) : base(frame)
    {
        BackgroundColor = UIColor.SystemBackground;
        AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;

        titleLabel = new UILabel
        {
            Text = "MEMORY OVERVIEW",
            Font = UIFont.BoldSystemFontOfSize(16),
            TextColor = UIColor.Label,
            Lines = 1
        };

        overlayButton = CreatePillButton("OVERLAY");
        overlayButton.TouchUpInside += (_, _) => ToggleOverlay();

        if (options.SaveSnapshotAction != null)
        {
            var action = options.SaveSnapshotAction;
            var label = string.IsNullOrWhiteSpace(action.Label) ? "COPY" : action.Label;
            copyButton = CreatePillButton(label);
            copyButton.TouchUpInside += async (_, _) => await ExecuteSaveSnapshotAsync(dataSink, action);
            AddSubview(copyButton);
        }

        chart = new NativeChartViewIos(new CGRect(0, 0, frame.Width, ChartHeight))
        {
            DataSink = dataSink,
            RenderMode = ChartRenderMode.Inline,
            WindowDuration = TimeSpan.FromSeconds(60),
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth
        };

        table = new UITableView
        {
            SeparatorStyle = UITableViewCellSeparatorStyle.SingleLine,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
        };
        eventsSource = new EventsTableSource(dataSink, table);
        table.Source = eventsSource;

        AddSubview(titleLabel);
        AddSubview(overlayButton);
        AddSubview(chart);
        AddSubview(table);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        var safe = SafeAreaInsets;
        var width = Bounds.Width;
        var y = safe.Top + VerticalPadding;
        var availableRight = width - HorizontalPadding;

        if (copyButton != null)
        {
            var copySize = copyButton.SizeThatFits(new CGSize(width, ButtonHeight));
            var copyWidth = (nfloat)Math.Max(copySize.Width + 12, 80);
            copyButton.Frame = new CGRect(availableRight - copyWidth, y, copyWidth, ButtonHeight);
            availableRight -= copyWidth + 8;
        }

        var overlaySize = overlayButton.SizeThatFits(new CGSize(width, ButtonHeight));
        var overlayWidth = (nfloat)Math.Max(overlaySize.Width + 12, 90);
        overlayButton.Frame = new CGRect(availableRight - overlayWidth, y, overlayWidth, ButtonHeight);

        var titleSize = titleLabel.SizeThatFits(new CGSize(width, ButtonHeight));
        var titleY = y + (ButtonHeight - titleSize.Height) / 2;
        titleLabel.Frame = new CGRect(HorizontalPadding, titleY, titleSize.Width, titleSize.Height);

        var headerBottom = y + ButtonHeight;
        y = headerBottom + SectionSpacing;

        chart.Frame = new CGRect(0, y, width, ChartHeight);
        y += ChartHeight;

        var tableHeight = Bounds.Height - safe.Bottom - y;
        table.Frame = new CGRect(0, y, width, (nfloat)Math.Max(0, tableHeight));
    }

    public override void WillMoveToWindow(UIWindow? window)
    {
        base.WillMoveToWindow(window);

        if (window != null)
        {
            eventsSource.BindEvents();
        }
        else
        {
            eventsSource.UnbindEvents();
        }
    }

    private static UIColor ToUiColor(Color color)
    {
        return UIColor.FromRGBA(color.RedNormalized, color.GreenNormalized, color.BlueNormalized, color.AlphaNormalized);
    }

    private static UIButton CreatePillButton(string text)
    {
        var button = new UIButton(UIButtonType.System);
        button.SetTitle(text, UIControlState.Normal);
        button.SetTitleColor(UIColor.White, UIControlState.Normal);
        button.TitleLabel.Font = UIFont.BoldSystemFontOfSize(14);
        button.BackgroundColor = ToUiColor(Constants.BrandColor_Faded);
        button.Layer.CornerRadius = 10;
        button.ClipsToBounds = true;
        button.ContentEdgeInsets = new UIEdgeInsets(6, 12, 6, 12);
        return button;
    }

    private static void ToggleOverlay()
    {
        if (Runtime.IsChartOverlayPresented)
        {
            Runtime.DismissOverlay();
        }
        else
        {
            Runtime.PresentOverlay();
        }
    }

    private static async Task ExecuteSaveSnapshotAsync(IDataSink dataSink, SaveSnapshotAction action)
    {
        try
        {
            var snapshot = dataSink.Snapshot();
            await action.CopyDelegate(snapshot);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to execute save snapshot action.");
            Logger.Exception(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            eventsSource.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class EventsTableSource : UITableViewSource, IDisposable
{
    private readonly IDataSink sink;
    private readonly UITableView table;
    private List<AppEventDisplay> cache = new();

    public EventsTableSource(IDataSink sink, UITableView table)
    {
        this.sink = sink;
        this.table = table;
    }

    public void BindEvents()
    {
        UnbindEvents();
        sink.OnEventsUpdated += OnEventsUpdated;
        Refresh();
    }

    public void UnbindEvents()
    {
        sink.OnEventsUpdated -= OnEventsUpdated;
    }

    private void OnEventsUpdated(object? sender, AppEventsUpdatedEventArgs e) => Refresh();

    private void Refresh()
    {
        var channelLookup = sink.Channels?.ToDictionary(c => c.Id) ?? new Dictionary<byte, Channel>();
        cache = sink.Events
                    .OrderByDescending(e => e.CapturedAtUtc)
                    .Take(50)
                    .Select(e => new AppEventDisplay
                    {
                        Label = e.Label,
                        Symbol = AppEventLegend.GetSymbol(e.Type),
                        ChannelColor = channelLookup.TryGetValue(e.Channel, out var channel) ? channel.Color : new Color(0, 0, 0),
                        Details = e.Details,
                        HasDetails = !string.IsNullOrWhiteSpace(e.Details),
                        Timestamp = e.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss")
                    })
                    .ToList();

        UIApplication.SharedApplication.InvokeOnMainThread(table.ReloadData);
    }

    public override nint RowsInSection(UITableView tableView, nint section) => cache.Count;

    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        var cell = tableView.DequeueReusableCell("ansight-event") ?? new UITableViewCell(UITableViewCellStyle.Subtitle, "ansight-event");
        if (indexPath.Row < cache.Count)
        {
            var display = cache[indexPath.Row];
            cell.TextLabel.Text = $"{display.Symbol} {display.Label}";
            cell.DetailTextLabel.Text = display.HasDetails ? $"{display.Timestamp} • {display.Details}" : display.Timestamp;
        }
        return cell;
    }

    public void Dispose()
    {
        UnbindEvents();
    }
}
#endif
