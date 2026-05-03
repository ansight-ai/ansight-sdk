#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.OS;
using Java.Lang;
using System.Runtime.CompilerServices;
using Object = Java.Lang.Object;

namespace Ansight.Platforms.Android;

/// <summary>
/// Registers Android lifecycle callbacks that keep <see cref="Runtime"/> lifecycle state in sync.
/// </summary>
public static class AndroidAppLifecycleTracker
{
    private static readonly ConditionalWeakTable<Application, TrackerCallbacks> trackers = new();

    public static void Register(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (trackers.TryGetValue(application, out _))
        {
            return;
        }

        var tracker = new TrackerCallbacks();
        trackers.Add(application, tracker);
        application.RegisterActivityLifecycleCallbacks(tracker);
        application.RegisterComponentCallbacks(tracker);
    }

    private sealed class TrackerCallbacks : Object, Application.IActivityLifecycleCallbacks, IComponentCallbacks2
    {
        public void OnActivityCreated(Activity? activity, Bundle? savedInstanceState)
        {
        }

        public void OnActivityDestroyed(Activity? activity)
        {
        }

        public void OnActivityPaused(Activity? activity)
        {
        }

        public void OnActivityResumed(Activity? activity)
        {
            Runtime.SetAppLifecycleState(AppLifecycleState.Foreground);
        }

        public void OnActivitySaveInstanceState(Activity? activity, Bundle? outState)
        {
        }

        public void OnActivityStarted(Activity? activity)
        {
        }

        public void OnActivityStopped(Activity? activity)
        {
        }

        public void OnConfigurationChanged(Configuration? newConfig)
        {
        }

        public void OnLowMemory()
        {
        }

        public void OnTrimMemory(TrimMemory level)
        {
            if (level == TrimMemory.UiHidden)
            {
                Runtime.SetAppLifecycleState(AppLifecycleState.Background);
            }
        }
    }
}
#endif
