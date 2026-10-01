#if IOS || MACCATALYST
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Ansight.Input;

internal sealed class AppleTouchCaptureSession : ITouchCaptureSession
{
    private readonly TouchCaptureOptions options;
    private readonly Action<CapturedTouch> recordTouch;
    private readonly Lock sync = new();
    private readonly List<InstalledRecognizer> installedRecognizers = [];
    private NSObject? windowDidBecomeKeyObserver;
    private NSObject? applicationDidBecomeActiveObserver;
    private bool started;

    public AppleTouchCaptureSession(TouchCaptureOptions options, Action<CapturedTouch> recordTouch)
    {
        this.options = options.Clone();
        this.recordTouch = recordTouch ?? throw new ArgumentNullException(nameof(recordTouch));
    }

    public void Start()
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (started)
            {
                return;
            }

            started = true;
            windowDidBecomeKeyObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                UIWindow.DidBecomeKeyNotification,
                _ => InstallCurrentWindows());
            applicationDidBecomeActiveObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                UIApplication.DidBecomeActiveNotification,
                _ => InstallCurrentWindows());
            InstallCurrentWindows();
        });
    }

    public void Stop()
    {
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            if (!started)
            {
                return;
            }

            started = false;

            if (windowDidBecomeKeyObserver is not null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(windowDidBecomeKeyObserver);
                windowDidBecomeKeyObserver.Dispose();
                windowDidBecomeKeyObserver = null;
            }

            if (applicationDidBecomeActiveObserver is not null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(applicationDidBecomeActiveObserver);
                applicationDidBecomeActiveObserver.Dispose();
                applicationDidBecomeActiveObserver = null;
            }

            InstalledRecognizer[] recognizers;
            lock (sync)
            {
                recognizers = installedRecognizers.ToArray();
                installedRecognizers.Clear();
            }

            foreach (var installed in recognizers)
            {
                installed.Window.RemoveGestureRecognizer(installed.Recognizer);
                installed.Dispose();
            }
        });
    }

    public void Dispose()
    {
        Stop();
    }

    private void InstallCurrentWindows()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene)
            {
                continue;
            }

            foreach (var window in windowScene.Windows)
            {
                if (window is not null && !window.Hidden)
                {
                    Install(window);
                }
            }
        }
    }

    private void Install(UIWindow window)
    {
        lock (sync)
        {
            if (!started || installedRecognizers.Any(installed => ReferenceEquals(installed.Window, window)))
            {
                return;
            }

            var recognizerDelegate = new SimultaneousGestureDelegate();
            var recognizer = new WindowTouchCaptureRecognizer(options, recordTouch)
            {
                CancelsTouchesInView = false,
                DelaysTouchesBegan = false,
                DelaysTouchesEnded = false,
                Delegate = recognizerDelegate
            };

            window.AddGestureRecognizer(recognizer);
            installedRecognizers.Add(new InstalledRecognizer(window, recognizer, recognizerDelegate));
            if (OperatingSystem.IsIOSVersionAtLeast(16, 4) ||
                OperatingSystem.IsMacCatalystVersionAtLeast(16, 4))
            {
                var hoverDelegate = new SimultaneousGestureDelegate();
                var hover = new WindowHoverCaptureRecognizer(recordTouch)
                {
                    Delegate = hoverDelegate,
                    CancelsTouchesInView = false
                };
                window.AddGestureRecognizer(hover);
                installedRecognizers.Add(new InstalledRecognizer(window, hover, hoverDelegate));
            }
        }
    }

    private sealed class WindowHoverCaptureRecognizer : UIHoverGestureRecognizer
    {
        private readonly Action<CapturedTouch> recordTouch;

        public WindowHoverCaptureRecognizer(Action<CapturedTouch> recordTouch)
            : base(null!, null!)
        {
            this.recordTouch = recordTouch;
            AllowedTouchTypes = [NSNumber.FromInt32((int)UITouchType.Stylus)];
            AddTarget(this, new Selector("captureHover:"));
        }

        [Export("captureHover:")]
        private void CaptureHover(UIHoverGestureRecognizer recognizer)
        {
            if (View is not UIWindow window)
            {
                return;
            }

            var action = State switch
            {
                UIGestureRecognizerState.Began => CapturedTouchAction.HoverEnter,
                UIGestureRecognizerState.Changed => CapturedTouchAction.HoverMove,
                UIGestureRecognizerState.Ended or UIGestureRecognizerState.Cancelled => CapturedTouchAction.HoverExit,
                _ => (CapturedTouchAction?)null
            };
            if (action is null)
            {
                return;
            }

            try
            {
                var point = LocationInView(window);
                recordTouch(new CapturedTouch(
                    action.Value,
                    (long)(nint)Handle,
                    0,
                    1,
                    point.X,
                    point.Y,
                    window.Bounds.Width,
                    window.Bounds.Height,
                    "points",
                    window.Screen?.Scale ?? UIScreen.MainScreen.Scale,
                    DateTimeOffset.UtcNow,
                    new TouchSampleDetails
                    {
                        Tool = "stylus",
                        SampleKind = "hover",
                        AltitudeRadians = AltitudeAngle,
                        AzimuthRadians = GetAzimuthAngle(window),
                        RollRadians = OperatingSystem.IsIOSVersionAtLeast(17, 5) ||
                            OperatingSystem.IsMacCatalystVersionAtLeast(17, 5)
                            ? RollAngle
                            : null,
                        Distance = ZOffset
                    }));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Apple Pencil hover capture skipped: {ex.Message}");
            }
        }
    }

    private sealed class WindowTouchCaptureRecognizer : UIGestureRecognizer
    {
        private readonly TouchCaptureOptions options;
        private readonly TouchMoveThrottle moveThrottle;
        private readonly Action<CapturedTouch> recordTouch;
        private readonly HashSet<nint> activeTouchHandles = [];

        public WindowTouchCaptureRecognizer(TouchCaptureOptions options, Action<CapturedTouch> recordTouch)
        {
            this.options = options;
            moveThrottle = new TouchMoveThrottle(options);
            this.recordTouch = recordTouch;
        }

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            RecordTouches(touches, CapturedTouchAction.Down, evt);
            var beginsGesture = activeTouchHandles.Count == 0;
            AddActiveTouches(touches);
            State = beginsGesture
                ? UIGestureRecognizerState.Began
                : UIGestureRecognizerState.Changed;
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            if (options.CaptureMoveEvents)
            {
                RecordTouches(touches, CapturedTouchAction.Move, evt);
            }

            State = UIGestureRecognizerState.Changed;
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            RecordTouches(touches, CapturedTouchAction.Up, evt);
            RemoveActiveTouches(touches);
            State = activeTouchHandles.Count == 0
                ? UIGestureRecognizerState.Ended
                : UIGestureRecognizerState.Changed;
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt)
        {
            if (options.CaptureCancelEvents)
            {
                RecordTouches(touches, CapturedTouchAction.Cancel, evt);
            }

            activeTouchHandles.Clear();
            State = UIGestureRecognizerState.Cancelled;
        }

        public override void TouchesEstimatedPropertiesUpdated(NSSet touches)
        {
            RecordTouches(touches, CapturedTouchAction.Move, null, "estimatedUpdate");
        }

        public override void Reset()
        {
            activeTouchHandles.Clear();
            base.Reset();
        }

        private void AddActiveTouches(NSSet touches)
        {
            foreach (var item in touches)
            {
                if (item is UITouch touch)
                {
                    activeTouchHandles.Add(touch.Handle);
                }
            }
        }

        private void RemoveActiveTouches(NSSet touches)
        {
            foreach (var item in touches)
            {
                if (item is UITouch touch)
                {
                    activeTouchHandles.Remove(touch.Handle);
                }
            }
        }

        private void RecordTouches(
            NSSet touches,
            CapturedTouchAction action,
            UIEvent? evt,
            string? sampleKindOverride = null)
        {
            if (View is not UIWindow window)
            {
                return;
            }

            var pointerIndex = 0;
            var pointerCount = (int)touches.Count;
            foreach (var item in touches)
            {
                if (item is not UITouch touch)
                {
                    continue;
                }

                try
                {
                    var isPencil = touch.Type == UITouchType.Stylus;
                    var samples = action == CapturedTouchAction.Move && isPencil && sampleKindOverride is null
                        ? evt?.GetCoalescedTouches(touch) ?? [touch]
                        : [touch];
                    foreach (var sample in samples)
                    {
                        var point = sample.LocationInView(window);
                        var details = new TouchSampleDetails
                        {
                            Tool = sample.Type switch
                            {
                                UITouchType.Stylus => "stylus",
                                UITouchType.Direct => "finger",
                                _ => "unknown"
                            },
                            SampleKind = sampleKindOverride ?? (sample.Timestamp == touch.Timestamp ? "current" : "coalesced"),
                            Force = isPencil ? sample.Force : null,
                            MaximumPossibleForce = isPencil ? sample.MaximumPossibleForce : null,
                            AltitudeRadians = isPencil ? sample.AltitudeAngle : null,
                            AzimuthRadians = isPencil ? sample.GetAzimuthAngle(window) : null,
                            RollRadians = isPencil &&
                                (OperatingSystem.IsIOSVersionAtLeast(17, 5) ||
                                 OperatingSystem.IsMacCatalystVersionAtLeast(17, 5))
                                ? sample.RollAngle
                                : null,
                            EstimatedProperties = isPencil ? (long)sample.EstimatedProperties : null,
                            EstimatedPropertiesExpectingUpdates = isPencil ? (long)sample.EstimatedPropertiesExpectingUpdates : null,
                            EstimationUpdateIndex = isPencil ? sample.EstimationUpdateIndex?.LongValue : null
                        };
                        var capturedTouch = new CapturedTouch(
                            action,
                            (long)(nint)touch.Handle,
                            pointerIndex,
                            pointerCount,
                            point.X,
                            point.Y,
                            window.Bounds.Width,
                            window.Bounds.Height,
                            "points",
                            window.Screen?.Scale ?? UIScreen.MainScreen.Scale,
                            DateTimeOffset.UtcNow.AddSeconds(sample.Timestamp - NSProcessInfo.ProcessInfo.SystemUptime),
                            details);

                        RecordCapturedTouch(capturedTouch);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Apple touch capture skipped: {ex.Message}");
                }

                pointerIndex++;
            }
        }

        private void RecordCapturedTouch(CapturedTouch capturedTouch)
        {
            if (capturedTouch.Details?.Tool != "stylus" && !moveThrottle.ShouldRecord(capturedTouch))
            {
                return;
            }

            recordTouch(capturedTouch);
            moveThrottle.ObserveRecorded(capturedTouch);
        }
    }

    private sealed class SimultaneousGestureDelegate : UIGestureRecognizerDelegate
    {
        public override bool ShouldRecognizeSimultaneously(UIGestureRecognizer gestureRecognizer, UIGestureRecognizer otherGestureRecognizer)
        {
            return true;
        }
    }

    private sealed class InstalledRecognizer : IDisposable
    {
        public InstalledRecognizer(
            UIWindow window,
            UIGestureRecognizer recognizer,
            UIGestureRecognizerDelegate recognizerDelegate)
        {
            Window = window;
            Recognizer = recognizer;
            RecognizerDelegate = recognizerDelegate;
        }

        public UIWindow Window { get; }

        public UIGestureRecognizer Recognizer { get; }

        private UIGestureRecognizerDelegate RecognizerDelegate { get; }

        public void Dispose()
        {
            Recognizer.Delegate = null!;
            Recognizer.Dispose();
            RecognizerDelegate.Dispose();
        }
    }
}
#endif
