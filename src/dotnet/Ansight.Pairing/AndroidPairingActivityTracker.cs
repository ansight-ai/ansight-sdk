#if ANDROID
using Android.App;
using Android.OS;

namespace Ansight;

internal sealed class AndroidPairingActivityTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private static readonly object sync = new();
    private static AndroidPairingActivityTracker? instance;
    private WeakReference<Activity>? currentActivity;

    internal static Activity? GetCurrentActivity()
    {
        EnsureRegistered();
        return instance?.TryGetCurrentActivity();
    }

    private static void EnsureRegistered()
    {
        if (instance is not null)
        {
            return;
        }

        lock (sync)
        {
            if (instance is not null)
            {
                return;
            }

            if (Application.Context is not Application application)
            {
                return;
            }

            var tracker = new AndroidPairingActivityTracker();
            application.RegisterActivityLifecycleCallbacks(tracker);
            instance = tracker;
        }
    }

    private Activity? TryGetCurrentActivity()
    {
        if (currentActivity?.TryGetTarget(out var activity) == true &&
            !activity.IsFinishing &&
            !activity.IsDestroyed)
        {
            return activity;
        }

        return null;
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
        => currentActivity = new WeakReference<Activity>(activity);

    public void OnActivityStarted(Activity activity)
        => currentActivity = new WeakReference<Activity>(activity);

    public void OnActivityResumed(Activity activity)
        => currentActivity = new WeakReference<Activity>(activity);

    public void OnActivityPaused(Activity activity)
    {
    }

    public void OnActivityStopped(Activity activity)
    {
    }

    public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
    {
    }

    public void OnActivityDestroyed(Activity activity)
    {
        if (currentActivity?.TryGetTarget(out var current) == true &&
            ReferenceEquals(current, activity))
        {
            currentActivity = null;
        }
    }
}
#endif
