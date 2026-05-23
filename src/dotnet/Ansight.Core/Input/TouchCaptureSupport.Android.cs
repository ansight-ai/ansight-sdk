#if ANDROID
using Android.App;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using Java.Lang;
using Object = Java.Lang.Object;

namespace Ansight.Input;

internal sealed class AndroidTouchCaptureSession : ITouchCaptureSession
{
    private static readonly Handler mainHandler = new(Looper.MainLooper!);

    private readonly TouchCaptureOptions options;
    private readonly System.Action<CapturedTouch> recordTouch;
    private readonly Lock sync = new();
    private readonly Dictionary<Activity, TouchCaptureWindowCallback> installedCallbacks = new();
    private ActivityCallbacks? activityCallbacks;
    private bool started;

    public AndroidTouchCaptureSession(TouchCaptureOptions options, System.Action<CapturedTouch> recordTouch)
    {
        this.options = options.Clone();
        this.recordTouch = recordTouch ?? throw new ArgumentNullException(nameof(recordTouch));
    }

    public void Start()
    {
        RunOnMainThread(() =>
        {
            if (started)
            {
                return;
            }

            started = true;

            if (Application.Context is Application application)
            {
                activityCallbacks = new ActivityCallbacks(this);
                application.RegisterActivityLifecycleCallbacks(activityCallbacks);
            }

            var currentRoot = AndroidSceneCapture.GetCurrentRoot();
            if (currentRoot is not null)
            {
                Install(currentRoot.Activity);
            }
        });
    }

    public void Stop()
    {
        RunOnMainThread(() =>
        {
            if (!started)
            {
                return;
            }

            started = false;

            if (Application.Context is Application application && activityCallbacks is not null)
            {
                application.UnregisterActivityLifecycleCallbacks(activityCallbacks);
                activityCallbacks.Dispose();
                activityCallbacks = null;
            }

            TouchCaptureWindowCallback[] callbacks;
            lock (sync)
            {
                callbacks = installedCallbacks.Values.ToArray();
                installedCallbacks.Clear();
            }

            foreach (var callback in callbacks)
            {
                callback.RestoreIfCurrent();
                callback.Dispose();
            }
        });
    }

    public void Dispose()
    {
        Stop();
    }

    private void Install(Activity? activity)
    {
        if (activity?.Window is null)
        {
            return;
        }

        lock (sync)
        {
            if (!started || installedCallbacks.ContainsKey(activity))
            {
                return;
            }

            var currentCallback = activity.Window.Callback;
            if (currentCallback is null || currentCallback is TouchCaptureWindowCallback)
            {
                return;
            }

            var callback = new TouchCaptureWindowCallback(activity, currentCallback, options, recordTouch);
            installedCallbacks[activity] = callback;
            activity.Window.Callback = callback;
        }
    }

    private void Uninstall(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        TouchCaptureWindowCallback? callback;
        lock (sync)
        {
            if (!installedCallbacks.Remove(activity, out callback))
            {
                return;
            }
        }

        callback.RestoreIfCurrent();
        callback.Dispose();
    }

    private static void RunOnMainThread(System.Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return;
        }

        mainHandler.Post(action);
    }

    private sealed class ActivityCallbacks : Object, Application.IActivityLifecycleCallbacks
    {
        private readonly AndroidTouchCaptureSession session;

        public ActivityCallbacks(AndroidTouchCaptureSession session)
        {
            this.session = session;
        }

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
        {
            session.Install(activity);
        }

        public void OnActivityDestroyed(Activity activity)
        {
            session.Uninstall(activity);
        }

        public void OnActivityPaused(Activity activity)
        {
        }

        public void OnActivityResumed(Activity activity)
        {
            session.Install(activity);
        }

        public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
        {
        }

        public void OnActivityStarted(Activity activity)
        {
            session.Install(activity);
        }

        public void OnActivityStopped(Activity activity)
        {
        }
    }

    private sealed class TouchCaptureWindowCallback : Object, Window.ICallback
    {
        private readonly Activity activity;
        private readonly Window.ICallback inner;
        private readonly TouchCaptureOptions options;
        private readonly TouchMoveThrottle moveThrottle;
        private readonly System.Action<CapturedTouch> recordTouch;

        public TouchCaptureWindowCallback(
            Activity activity,
            Window.ICallback inner,
            TouchCaptureOptions options,
            System.Action<CapturedTouch> recordTouch)
        {
            this.activity = activity;
            this.inner = inner;
            this.options = options;
            moveThrottle = new TouchMoveThrottle(options);
            this.recordTouch = recordTouch;
        }

        public void RestoreIfCurrent()
        {
            if (activity.Window?.Callback == this)
            {
                activity.Window.Callback = inner;
            }
        }

        public bool DispatchTouchEvent(MotionEvent? e)
        {
            Capture(e);
            return inner.DispatchTouchEvent(e);
        }

        public bool DispatchGenericMotionEvent(MotionEvent? e) => inner.DispatchGenericMotionEvent(e);

        public bool DispatchKeyEvent(KeyEvent? e) => inner.DispatchKeyEvent(e);

        public bool DispatchKeyShortcutEvent(KeyEvent? e) => inner.DispatchKeyShortcutEvent(e);

        public bool DispatchPopulateAccessibilityEvent(AccessibilityEvent? e) => inner.DispatchPopulateAccessibilityEvent(e);

        public bool DispatchTrackballEvent(MotionEvent? e) => inner.DispatchTrackballEvent(e);

        public void OnActionModeFinished(ActionMode? mode) => inner.OnActionModeFinished(mode);

        public void OnActionModeStarted(ActionMode? mode) => inner.OnActionModeStarted(mode);

        public void OnAttachedToWindow() => inner.OnAttachedToWindow();

        public void OnContentChanged() => inner.OnContentChanged();

        public bool OnCreatePanelMenu(int featureId, IMenu? menu) => inner.OnCreatePanelMenu(featureId, menu!);

        public View? OnCreatePanelView(int featureId) => inner.OnCreatePanelView(featureId);

        public void OnDetachedFromWindow() => inner.OnDetachedFromWindow();

        public bool OnMenuItemSelected(int featureId, IMenuItem? item) => inner.OnMenuItemSelected(featureId, item!);

        public bool OnMenuOpened(int featureId, IMenu? menu) => inner.OnMenuOpened(featureId, menu!);

        public void OnPanelClosed(int featureId, IMenu? menu) => inner.OnPanelClosed(featureId, menu!);

        public void OnPointerCaptureChanged(bool hasCapture)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                inner.OnPointerCaptureChanged(hasCapture);
            }
        }

        public void OnProvideKeyboardShortcuts(IList<KeyboardShortcutGroup>? data, IMenu? menu, int deviceId)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                inner.OnProvideKeyboardShortcuts(data, menu, deviceId);
            }
        }

        public bool OnPreparePanel(int featureId, View? view, IMenu? menu) => inner.OnPreparePanel(featureId, view, menu!);

        public bool OnSearchRequested() => inner.OnSearchRequested();

        public bool OnSearchRequested(SearchEvent? searchEvent)
        {
            return OperatingSystem.IsAndroidVersionAtLeast(23)
                ? inner.OnSearchRequested(searchEvent)
                : inner.OnSearchRequested();
        }

        public void OnWindowAttributesChanged(WindowManagerLayoutParams? attrs) => inner.OnWindowAttributesChanged(attrs);

        public void OnWindowFocusChanged(bool hasFocus) => inner.OnWindowFocusChanged(hasFocus);

        public ActionMode? OnWindowStartingActionMode(ActionMode.ICallback? callback)
            => inner.OnWindowStartingActionMode(callback);

        public ActionMode? OnWindowStartingActionMode(ActionMode.ICallback? callback, ActionModeType type)
        {
            return OperatingSystem.IsAndroidVersionAtLeast(23)
                ? inner.OnWindowStartingActionMode(callback, type)
                : inner.OnWindowStartingActionMode(callback);
        }

        private void Capture(MotionEvent? motionEvent)
        {
            if (motionEvent is null)
            {
                return;
            }

            try
            {
                var action = motionEvent.ActionMasked;
                switch (action)
                {
                    case MotionEventActions.Down:
                    case MotionEventActions.PointerDown:
                        RecordPointer(motionEvent, CapturedTouchAction.Down, motionEvent.ActionIndex);
                        break;
                    case MotionEventActions.Move:
                        if (options.CaptureMoveEvents)
                        {
                            RecordAllPointers(motionEvent, CapturedTouchAction.Move);
                        }
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.PointerUp:
                        RecordPointer(motionEvent, CapturedTouchAction.Up, motionEvent.ActionIndex);
                        break;
                    case MotionEventActions.Cancel:
                        if (options.CaptureCancelEvents)
                        {
                            RecordAllPointers(motionEvent, CapturedTouchAction.Cancel);
                        }
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"Android touch capture skipped: {ex.Message}");
            }
        }

        private void RecordAllPointers(MotionEvent motionEvent, CapturedTouchAction action)
        {
            for (var index = 0; index < motionEvent.PointerCount; index++)
            {
                RecordPointer(motionEvent, action, index);
            }
        }

        private void RecordPointer(MotionEvent motionEvent, CapturedTouchAction action, int pointerIndex)
        {
            if (pointerIndex < 0 || pointerIndex >= motionEvent.PointerCount)
            {
                return;
            }

            var rootView = activity.Window?.DecorView?.RootView;
            var surfaceWidth = rootView?.Width > 0 ? rootView.Width : (int?)null;
            var surfaceHeight = rootView?.Height > 0 ? rootView.Height : (int?)null;
            var density = activity.Resources?.DisplayMetrics?.Density;

            var capturedTouch = new CapturedTouch(
                action,
                motionEvent.GetPointerId(pointerIndex),
                pointerIndex,
                motionEvent.PointerCount,
                motionEvent.GetX(pointerIndex),
                motionEvent.GetY(pointerIndex),
                surfaceWidth,
                surfaceHeight,
                "pixels",
                density,
                DateTimeOffset.UtcNow);

            RecordCapturedTouch(capturedTouch);
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
}
#endif
