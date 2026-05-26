#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using Java.Lang;
using Object = Java.Lang.Object;

namespace Ansight.Input;

internal sealed class AndroidTouchCaptureSession : ITouchCaptureSession
{
    private const long WindowScanIntervalMilliseconds = 500;

    private static readonly Handler mainHandler = new(Looper.MainLooper!);

    private readonly TouchCaptureOptions options;
    private readonly System.Action<CapturedTouch> recordTouch;
    private readonly Lock sync = new();
    private readonly Dictionary<nint, TouchCaptureWindowCallback> installedCallbacks = new();
    private readonly ScanRunnable scanRunnable;
    private ActivityCallbacks? activityCallbacks;
    private bool scanScheduled;
    private bool started;

    public AndroidTouchCaptureSession(TouchCaptureOptions options, System.Action<CapturedTouch> recordTouch)
    {
        this.options = options.Clone();
        this.recordTouch = recordTouch ?? throw new ArgumentNullException(nameof(recordTouch));
        scanRunnable = new ScanRunnable(this);
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

            ScheduleWindowScan();
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
            scanScheduled = false;
            mainHandler.RemoveCallbacks(scanRunnable);

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

        var activeWindowHandles = new HashSet<nint>();
        activeWindowHandles.Add(activity.Window.Handle);
        InstallWindow(activity, activity.Window, () => activity.Window?.DecorView?.RootView);

        foreach (var topLevelView in AndroidSceneCapture.GetTopLevelViews(activity))
        {
            var window = TryGetWindowForTopLevelView(topLevelView);
            if (window != null)
            {
                activeWindowHandles.Add(window.Handle);
            }

            InstallWindow(activity, window, () => topLevelView);
        }

        PruneDetachedWindows(activity, activeWindowHandles);
    }

    private void InstallWindow(Activity activity, Window? window, Func<View?> eventRootViewProvider)
    {
        if (window == null || window.Handle == IntPtr.Zero)
        {
            return;
        }

        lock (sync)
        {
            var windowHandle = window.Handle;
            if (!started || installedCallbacks.ContainsKey(windowHandle))
            {
                return;
            }

            Window.ICallback? currentCallback;
            try
            {
                currentCallback = window.Callback;
            }
            catch
            {
                return;
            }

            if (currentCallback is null || currentCallback is TouchCaptureWindowCallback)
            {
                return;
            }

            var callback = new TouchCaptureWindowCallback(activity, window, currentCallback, eventRootViewProvider, options, recordTouch);
            installedCallbacks[windowHandle] = callback;

            try
            {
                window.Callback = callback;
            }
            catch
            {
                installedCallbacks.Remove(windowHandle);
                callback.Dispose();
            }
        }
    }

    private void PruneDetachedWindows(Activity activity, HashSet<nint> activeWindowHandles)
    {
        List<TouchCaptureWindowCallback> callbacks;
        lock (sync)
        {
            callbacks = installedCallbacks.Values
                .Where(callback =>
                    IsSameJavaObject(callback.Activity, activity) &&
                    !activeWindowHandles.Contains(callback.WindowHandle))
                .ToList();

            foreach (var callback in callbacks)
            {
                installedCallbacks.Remove(callback.WindowHandle);
            }
        }

        foreach (var callback in callbacks)
        {
            callback.RestoreIfCurrent();
            callback.Dispose();
        }
    }

    private void Uninstall(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        List<TouchCaptureWindowCallback> callbacks;
        lock (sync)
        {
            callbacks = installedCallbacks.Values
                .Where(callback => IsSameJavaObject(callback.Activity, activity))
                .ToList();

            foreach (var installedCallback in callbacks)
            {
                installedCallbacks.Remove(installedCallback.WindowHandle);
            }
        }

        foreach (var installedCallback in callbacks)
        {
            installedCallback.RestoreIfCurrent();
            installedCallback.Dispose();
        }
    }

    private void ScheduleWindowScan()
    {
        lock (sync)
        {
            if (!started || scanScheduled)
            {
                return;
            }

            scanScheduled = true;
        }

        mainHandler.PostDelayed(scanRunnable, WindowScanIntervalMilliseconds);
    }

    private void RunWindowScan()
    {
        try
        {
            var currentRoot = AndroidSceneCapture.GetCurrentRoot();
            if (currentRoot is not null)
            {
                Install(currentRoot.Activity);
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning($"Android touch capture window scan skipped: {ex.Message}");
        }

        lock (sync)
        {
            scanScheduled = false;
        }

        ScheduleWindowScan();
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

    private static Window? TryGetWindowForTopLevelView(View view)
    {
        var window = TryGetWindowFromViewField(view);
        if (window != null)
        {
            return window;
        }

        return TryGetActivity(view.Context)?.Window;
    }

    private static Window? TryGetWindowFromViewField(View view)
    {
        var currentClass = view.Class;
        while (currentClass != null)
        {
            try
            {
                using var field = currentClass.GetDeclaredField("mWindow");
                field.Accessible = true;
                var value = field.Get(view);
                if (value is Window window)
                {
                    return window;
                }

                value?.Dispose();
            }
            catch
            {
            }

            currentClass = currentClass.Superclass;
        }

        return null;
    }

    private static Activity? TryGetActivity(Context? context)
    {
        while (context != null)
        {
            if (context is Activity activity)
            {
                return activity;
            }

            if (context is ContextWrapper contextWrapper &&
                contextWrapper.BaseContext != null &&
                !ReferenceEquals(context, contextWrapper.BaseContext))
            {
                context = contextWrapper.BaseContext;
                continue;
            }

            return null;
        }

        return null;
    }

    private static bool IsSameJavaObject(Java.Lang.Object? left, Java.Lang.Object? right)
    {
        return left != null &&
            right != null &&
            left.Handle != IntPtr.Zero &&
            right.Handle != IntPtr.Zero &&
            Android.Runtime.JNIEnv.IsSameObject(left.Handle, right.Handle);
    }

    private sealed class ScanRunnable : Object, IRunnable
    {
        private readonly AndroidTouchCaptureSession session;

        public ScanRunnable(AndroidTouchCaptureSession session)
        {
            this.session = session;
        }

        public void Run()
        {
            session.RunWindowScan();
        }
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
        private readonly Window window;
        private readonly Window.ICallback inner;
        private readonly Func<View?> eventRootViewProvider;
        private readonly TouchCaptureOptions options;
        private readonly TouchMoveThrottle moveThrottle;
        private readonly System.Action<CapturedTouch> recordTouch;

        public TouchCaptureWindowCallback(
            Activity activity,
            Window window,
            Window.ICallback inner,
            Func<View?> eventRootViewProvider,
            TouchCaptureOptions options,
            System.Action<CapturedTouch> recordTouch)
        {
            this.activity = activity;
            this.window = window;
            this.inner = inner;
            this.eventRootViewProvider = eventRootViewProvider;
            this.options = options;
            moveThrottle = new TouchMoveThrottle(options);
            this.recordTouch = recordTouch;
        }

        public Activity Activity => activity;

        public nint WindowHandle => window.Handle;

        public void RestoreIfCurrent()
        {
            try
            {
                if (window.Callback == this)
                {
                    window.Callback = inner;
                }
            }
            catch
            {
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

            var activityRootView = activity.Window?.DecorView?.RootView;
            var surfaceWidth = activityRootView?.Width > 0 ? activityRootView.Width : (int?)null;
            var surfaceHeight = activityRootView?.Height > 0 ? activityRootView.Height : (int?)null;
            var density = activity.Resources?.DisplayMetrics?.Density;
            var x = (double)motionEvent.GetX(pointerIndex);
            var y = (double)motionEvent.GetY(pointerIndex);

            var eventRootView = eventRootViewProvider();
            if (activityRootView != null &&
                eventRootView != null &&
                !IsSameJavaObject(activityRootView, eventRootView))
            {
                var activityLocation = GetViewLocationOnScreen(activityRootView);
                var eventLocation = GetViewLocationOnScreen(eventRootView);
                x += eventLocation.X - activityLocation.X;
                y += eventLocation.Y - activityLocation.Y;
            }

            var capturedTouch = new CapturedTouch(
                action,
                motionEvent.GetPointerId(pointerIndex),
                pointerIndex,
                motionEvent.PointerCount,
                x,
                y,
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

        private static ViewLocation GetViewLocationOnScreen(View view)
        {
            var location = new int[2];
            view.GetLocationOnScreen(location);
            return new ViewLocation(location[0], location[1]);
        }

        private readonly record struct ViewLocation(int X, int Y);
    }
}
#endif
