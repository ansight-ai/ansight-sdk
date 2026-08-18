#if IOS || MACCATALYST
using Foundation;
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
            RecordTouches(touches, CapturedTouchAction.Down);
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
                RecordTouches(touches, CapturedTouchAction.Move);
            }

            State = UIGestureRecognizerState.Changed;
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            RecordTouches(touches, CapturedTouchAction.Up);
            RemoveActiveTouches(touches);
            State = activeTouchHandles.Count == 0
                ? UIGestureRecognizerState.Ended
                : UIGestureRecognizerState.Changed;
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt)
        {
            if (options.CaptureCancelEvents)
            {
                RecordTouches(touches, CapturedTouchAction.Cancel);
            }

            activeTouchHandles.Clear();
            State = UIGestureRecognizerState.Cancelled;
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

        private void RecordTouches(NSSet touches, CapturedTouchAction action)
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
                    var point = touch.LocationInView(window);
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
                        DateTimeOffset.UtcNow);

                    RecordCapturedTouch(capturedTouch);
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
            if (!moveThrottle.ShouldRecord(capturedTouch))
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
