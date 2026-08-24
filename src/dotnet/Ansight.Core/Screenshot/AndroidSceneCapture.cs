#if ANDROID
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using System.Runtime.Versioning;

namespace Ansight.Screenshot;

internal sealed record AndroidSceneCaptureRoot(Activity Activity, View RootView);

internal sealed record AndroidSceneCaptureResult(bool Success, string? Error)
{
    internal static AndroidSceneCaptureResult FromSuccess() => new(true, null);

    internal static AndroidSceneCaptureResult FromFailure(string error) => new(false, error);
}

internal static class AndroidSceneCapture
{
    internal static void SetCurrentActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        AndroidActivityTracker.SetCurrentActivity(activity);
    }

    internal static AndroidSceneCaptureRoot? GetCurrentRoot()
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (activity == null || rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            return null;
        }

        return new AndroidSceneCaptureRoot(activity, rootView);
    }

    internal static async Task<AndroidSceneCaptureResult> CaptureAsync(
        AndroidSceneCaptureRoot captureRoot,
        Bitmap destinationBitmap,
        Canvas destinationCanvas,
        Bitmap windowBitmap,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captureRoot);
        ArgumentNullException.ThrowIfNull(destinationBitmap);
        ArgumentNullException.ThrowIfNull(destinationCanvas);
        ArgumentNullException.ThrowIfNull(windowBitmap);

        cancellationToken.ThrowIfCancellationRequested();

        if (captureRoot.RootView.Width <= 0 || captureRoot.RootView.Height <= 0)
        {
            return AndroidSceneCaptureResult.FromFailure("The Android root view has no drawable bounds.");
        }

        if (destinationBitmap.Width <= 0 || destinationBitmap.Height <= 0)
        {
            return AndroidSceneCaptureResult.FromFailure("The Android screenshot target has no drawable bounds.");
        }

        if (windowBitmap.Width != destinationBitmap.Width || windowBitmap.Height != destinationBitmap.Height)
        {
            return AndroidSceneCaptureResult.FromFailure("The Android window copy target must match the destination bitmap size.");
        }

        Clear(destinationCanvas);

        if (OperatingSystem.IsAndroidVersionAtLeast(26) &&
            captureRoot.Activity.Window != null &&
            await TryCapturePixelCopyCompositeAsync(
                captureRoot.Activity,
                captureRoot.RootView,
                destinationCanvas,
                windowBitmap,
                cancellationToken))
        {
            return AndroidSceneCaptureResult.FromSuccess();
        }

        Clear(destinationCanvas);
        await CaptureViewDrawFallbackAsync(
            captureRoot.Activity,
            captureRoot.RootView,
            destinationCanvas,
            destinationBitmap.Width,
            destinationBitmap.Height,
            cancellationToken);
        return AndroidSceneCaptureResult.FromSuccess();
    }

    [SupportedOSPlatform("android26.0")]
    private static async Task<bool> TryCapturePixelCopyCompositeAsync(
        Activity activity,
        View rootView,
        Canvas canvas,
        Bitmap windowBitmap,
        CancellationToken cancellationToken)
    {
        var rootLocation = GetViewLocationOnScreen(rootView);
        var scaleX = windowBitmap.Width / (float)rootView.Width;
        var scaleY = windowBitmap.Height / (float)rootView.Height;
        var overlaidSurfaceHandles = new HashSet<nint>();

        var windowCopyResult = await CopyWindowAsync(activity.Window!, windowBitmap, cancellationToken);
        if (windowCopyResult != (int)PixelCopyResult.Success)
        {
            return false;
        }

        canvas.DrawBitmap(windowBitmap, 0f, 0f, null);

        await OverlaySurfaceBackedChildrenAsync(
            canvas,
            rootView,
            rootLocation,
            scaleX,
            scaleY,
            overlaidSurfaceHandles,
            includeTextureViews: true,
            cancellationToken);
        await OverlayFragmentHostedSurfaceBackedViewsAsync(
            activity,
            canvas,
            rootLocation,
            scaleX,
            scaleY,
            overlaidSurfaceHandles,
            includeTextureViews: true,
            cancellationToken);

        foreach (var topLevelView in GetTopLevelViews(activity))
        {
            if (IsSameJavaObject(topLevelView, rootView))
            {
                continue;
            }

            DrawView(canvas, topLevelView, rootLocation, scaleX, scaleY);
            await OverlaySurfaceBackedChildrenAsync(
                canvas,
                topLevelView,
                rootLocation,
                scaleX,
                scaleY,
                overlaidSurfaceHandles,
                includeTextureViews: true,
                cancellationToken);
        }

        return true;
    }

    private static async Task CaptureViewDrawFallbackAsync(
        Activity activity,
        View rootView,
        Canvas canvas,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        var topLevelViews = GetTopLevelViews(activity);
        var rootLocation = GetViewLocationOnScreen(rootView);
        var scaleX = targetWidth / (float)rootView.Width;
        var scaleY = targetHeight / (float)rootView.Height;

        if (topLevelViews.Count == 0)
        {
            var fallbackSurfaceHandles = new HashSet<nint>();
            DrawView(canvas, rootView, rootLocation, scaleX, scaleY);
            await OverlaySurfaceBackedChildrenAsync(
                canvas,
                rootView,
                rootLocation,
                scaleX,
                scaleY,
                fallbackSurfaceHandles,
                includeTextureViews: true,
                cancellationToken);
            await OverlayFragmentHostedSurfaceBackedViewsAsync(
                activity,
                canvas,
                rootLocation,
                scaleX,
                scaleY,
                fallbackSurfaceHandles,
                includeTextureViews: true,
                cancellationToken);
            return;
        }

        var overlaidSurfaceHandles = new HashSet<nint>();
        var fragmentSurfacesOverlaid = false;
        foreach (var topLevelView in topLevelViews)
        {
            DrawView(canvas, topLevelView, rootLocation, scaleX, scaleY);
            await OverlaySurfaceBackedChildrenAsync(
                canvas,
                topLevelView,
                rootLocation,
                scaleX,
                scaleY,
                overlaidSurfaceHandles,
                includeTextureViews: true,
                cancellationToken);

            if (!fragmentSurfacesOverlaid && IsSameJavaObject(topLevelView, rootView))
            {
                await OverlayFragmentHostedSurfaceBackedViewsAsync(
                    activity,
                    canvas,
                    rootLocation,
                    scaleX,
                    scaleY,
                    overlaidSurfaceHandles,
                    includeTextureViews: true,
                    cancellationToken);
                fragmentSurfacesOverlaid = true;
            }
        }

        if (!fragmentSurfacesOverlaid)
        {
            await OverlayFragmentHostedSurfaceBackedViewsAsync(
                activity,
                canvas,
                rootLocation,
                scaleX,
                scaleY,
                overlaidSurfaceHandles,
                includeTextureViews: true,
                cancellationToken);
        }
    }

    [SupportedOSPlatform("android26.0")]
    private static async Task<int> CopyWindowAsync(Window window, Bitmap bitmap, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            PixelCopy.Request(
                window,
                bitmap,
                new PixelCopyFinishedListener(completion),
                PixelCopyThread.GetHandler());
        }
        catch
        {
            return -1;
        }

        return await completion.Task;
    }

    internal static List<View> GetTopLevelViews(Activity activity)
    {
        var topLevelViews = new List<View>();
        var activityRootView = activity.Window?.DecorView?.RootView;
        var packageName = activity.PackageName ?? string.Empty;

        try
        {
            var windowManagerGlobalClass = JNIEnv.FindClass("android/view/WindowManagerGlobal");
            if (windowManagerGlobalClass != IntPtr.Zero)
            {
                var getInstanceMethod = JNIEnv.GetStaticMethodID(windowManagerGlobalClass, "getInstance", "()Landroid/view/WindowManagerGlobal;");
                if (getInstanceMethod != IntPtr.Zero)
                {
                    var windowManagerGlobalHandle = JNIEnv.CallStaticObjectMethod(windowManagerGlobalClass, getInstanceMethod);
                    if (windowManagerGlobalHandle != IntPtr.Zero)
                    {
                        using var windowManagerGlobal = Java.Lang.Object.GetObject<Java.Lang.Object>(windowManagerGlobalHandle, JniHandleOwnership.TransferLocalRef);
                        if (windowManagerGlobal == null)
                        {
                            return topLevelViews;
                        }

                        var viewsField = JNIEnv.GetFieldID(windowManagerGlobalClass, "mViews", "Ljava/util/ArrayList;");
                        if (viewsField != IntPtr.Zero)
                        {
                            var viewsHandle = JNIEnv.GetObjectField(windowManagerGlobal.Handle, viewsField);
                            if (viewsHandle != IntPtr.Zero)
                            {
                                using var views = Java.Lang.Object.GetObject<JavaList<View>>(viewsHandle, JniHandleOwnership.TransferLocalRef);
                                if (views != null)
                                {
                                    foreach (var view in views)
                                    {
                                        if (view != null &&
                                            ShouldCaptureTopLevelView(view, activity, packageName) &&
                                            !topLevelViews.Any(existing => IsSameJavaObject(existing, view)))
                                        {
                                            topLevelViews.Add(view);
                                        }
                                    }
                                }
                            }
                        }

                        var rootsField = JNIEnv.GetFieldID(windowManagerGlobalClass, "mRoots", "Ljava/util/ArrayList;");
                        if (rootsField != IntPtr.Zero)
                        {
                            var rootsHandle = JNIEnv.GetObjectField(windowManagerGlobal.Handle, rootsField);
                            if (rootsHandle != IntPtr.Zero)
                            {
                                using var roots = Java.Lang.Object.GetObject<JavaList<Java.Lang.Object>>(rootsHandle, JniHandleOwnership.TransferLocalRef);
                                if (roots != null)
                                {
                                    foreach (var root in roots)
                                    {
                                        if (root == null || root.Handle == IntPtr.Zero)
                                        {
                                            continue;
                                        }

                                        var rootClass = JNIEnv.GetObjectClass(root.Handle);
                                        if (rootClass == IntPtr.Zero)
                                        {
                                            continue;
                                        }

                                        try
                                        {
                                            var viewField = JNIEnv.GetFieldID(rootClass, "mView", "Landroid/view/View;");
                                            if (viewField == IntPtr.Zero)
                                            {
                                                continue;
                                            }

                                            var viewHandle = JNIEnv.GetObjectField(root.Handle, viewField);
                                            if (viewHandle == IntPtr.Zero)
                                            {
                                                continue;
                                            }

                                            var view = Java.Lang.Object.GetObject<View>(viewHandle, JniHandleOwnership.TransferLocalRef);
                                            if (view != null &&
                                                ShouldCaptureTopLevelView(view, activity, packageName) &&
                                                !topLevelViews.Any(existing => IsSameJavaObject(existing, view)))
                                            {
                                                topLevelViews.Add(view);
                                            }
                                        }
                                        finally
                                        {
                                            JNIEnv.DeleteLocalRef(rootClass);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        if (activityRootView != null)
        {
            var orderedViews = new List<View> { activityRootView };
            foreach (var topLevelView in topLevelViews)
            {
                if (!orderedViews.Any(existing => IsSameJavaObject(existing, topLevelView)))
                {
                    orderedViews.Add(topLevelView);
                }
            }

            return orderedViews;
        }

        return topLevelViews;
    }

    private static bool ShouldCaptureTopLevelView(View? view, Activity activity, string packageName)
    {
        if (!IsVisibleForCapture(view))
        {
            return false;
        }

        if (BelongsToActivityContext(view!.Context, activity))
        {
            return true;
        }

        var context = view!.Context;
        while (context is ContextWrapper contextWrapper && contextWrapper.BaseContext != null && !ReferenceEquals(context, contextWrapper.BaseContext))
        {
            if (string.Equals(context.PackageName, packageName, StringComparison.Ordinal))
            {
                return true;
            }

            context = contextWrapper.BaseContext;
        }

        return string.Equals(context?.PackageName, packageName, StringComparison.Ordinal);
    }

    private static bool BelongsToActivityContext(Context? context, Activity activity)
    {
        while (context is ContextWrapper contextWrapper && contextWrapper.BaseContext != null && !ReferenceEquals(context, contextWrapper.BaseContext))
        {
            if (IsSameJavaObject(context as Java.Lang.Object, activity) ||
                IsSameJavaObject(contextWrapper.BaseContext as Java.Lang.Object, activity))
            {
                return true;
            }

            context = contextWrapper.BaseContext;
        }

        return IsSameJavaObject(context as Java.Lang.Object, activity);
    }

    private static bool IsSameJavaObject(Java.Lang.Object? left, Java.Lang.Object? right)
    {
        return left != null &&
            right != null &&
            left.Handle != IntPtr.Zero &&
            right.Handle != IntPtr.Zero &&
            JNIEnv.IsSameObject(left.Handle, right.Handle);
    }

    private static bool IsVisibleForCapture(View? view)
    {
        return view != null &&
            view.Visibility == ViewStates.Visible &&
            view.Alpha > 0 &&
            view.Width > 0 &&
            view.Height > 0;
    }

    private static void DrawView(Canvas canvas, View view, AndroidViewLocation rootLocation, float scaleX, float scaleY)
    {
        var location = GetViewLocationOnScreen(view);

        var saveCount = canvas.Save();
        try
        {
            canvas.Scale(scaleX, scaleY);
            canvas.Translate(location.X - rootLocation.X, location.Y - rootLocation.Y);
            view.Draw(canvas);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static async Task OverlaySurfaceBackedChildrenAsync(
        Canvas canvas,
        View rootView,
        AndroidViewLocation rootLocation,
        float scaleX,
        float scaleY,
        HashSet<nint> overlaidSurfaceHandles,
        bool includeTextureViews,
        CancellationToken cancellationToken)
    {
        var specialViews = new List<View>();
        CollectSurfaceBackedViews(rootView, specialViews, overlaidSurfaceHandles, includeTextureViews);
        foreach (var specialView in specialViews)
        {
            switch (specialView)
            {
                case SurfaceView surfaceView when OperatingSystem.IsAndroidVersionAtLeast(26):
                    await OverlaySurfaceViewAsync(canvas, surfaceView, rootLocation, scaleX, scaleY, cancellationToken);
                    break;
                case TextureView textureView when includeTextureViews:
                    OverlayTextureView(canvas, textureView, rootLocation, scaleX, scaleY);
                    break;
            }
        }
    }

    private static async Task OverlayFragmentHostedSurfaceBackedViewsAsync(
        Activity activity,
        Canvas canvas,
        AndroidViewLocation rootLocation,
        float scaleX,
        float scaleY,
        HashSet<nint> overlaidSurfaceHandles,
        bool includeTextureViews,
        CancellationToken cancellationToken)
    {
        foreach (var fragmentView in GetFragmentRootViews(activity))
        {
            await OverlaySurfaceBackedChildrenAsync(
                canvas,
                fragmentView,
                rootLocation,
                scaleX,
                scaleY,
                overlaidSurfaceHandles,
                includeTextureViews,
                cancellationToken);
        }
    }

    private static List<View> GetFragmentRootViews(Activity activity)
    {
        var fragmentRootViews = new List<View>();
        var visitedViewHandles = new HashSet<nint>();

        try
        {
            var supportFragmentManager = activity.GetType().GetProperty("SupportFragmentManager")?.GetValue(activity);
            if (supportFragmentManager != null)
            {
                CollectFragmentManagerRootViews(supportFragmentManager, fragmentRootViews, visitedViewHandles);

                foreach (var topLevelView in GetTopLevelViews(activity))
                {
                    CollectFragmentContainerRootViews(topLevelView, supportFragmentManager, fragmentRootViews, visitedViewHandles);
                }
            }
        }
        catch
        {
        }

        return fragmentRootViews;
    }

    private static void CollectFragmentManagerRootViews(object fragmentManager, List<View> results, HashSet<nint> visitedViewHandles)
    {
        if (fragmentManager.GetType().GetProperty("Fragments")?.GetValue(fragmentManager) is not System.Collections.IEnumerable fragments)
        {
            return;
        }

        foreach (var fragment in fragments)
        {
            CollectFragmentObject(fragment, results, visitedViewHandles);
        }
    }

    private static void CollectFragmentObject(object? fragment, List<View> results, HashSet<nint> visitedViewHandles)
    {
        if (fragment == null)
        {
            return;
        }

        if (fragment.GetType().GetProperty("View")?.GetValue(fragment) is View fragmentView &&
            IsVisibleForCapture(fragmentView) &&
            visitedViewHandles.Add(fragmentView.Handle))
        {
            results.Add(fragmentView);
        }

        var dialog = fragment.GetType().GetProperty("Dialog")?.GetValue(fragment);
        var dialogWindow = dialog?.GetType().GetProperty("Window")?.GetValue(dialog);
        if (dialogWindow?.GetType().GetProperty("DecorView")?.GetValue(dialogWindow) is View decorView &&
            IsVisibleForCapture(decorView) &&
            visitedViewHandles.Add(decorView.Handle))
        {
            results.Add(decorView);
        }

        var childFragmentManager = fragment.GetType().GetProperty("ChildFragmentManager")?.GetValue(fragment);
        if (childFragmentManager != null)
        {
            CollectFragmentManagerRootViews(childFragmentManager, results, visitedViewHandles);
        }
    }

    private static void CollectFragmentContainerRootViews(
        View rootView,
        object fragmentManager,
        List<View> results,
        HashSet<nint> visitedViewHandles)
    {
        foreach (var fragmentContainerView in GetFragmentContainerViews(rootView))
        {
            var fragment = TryFindFragmentById(fragmentManager, fragmentContainerView.Id);
            if (fragment != null)
            {
                CollectFragmentObject(fragment, results, visitedViewHandles);
            }
        }
    }

    private static List<View> GetFragmentContainerViews(View rootView)
    {
        var fragmentContainerViews = new List<View>();
        CollectFragmentContainerViews(rootView, fragmentContainerViews);
        return fragmentContainerViews;
    }

    private static void CollectFragmentContainerViews(View view, List<View> results)
    {
        if (!IsVisibleForCapture(view))
        {
            return;
        }

        var className = view.Class?.Name ?? view.GetType().FullName;
        if (string.Equals(className, "androidx.fragment.app.FragmentContainerView", StringComparison.Ordinal) ||
            string.Equals(view.Class?.SimpleName, "FragmentContainerView", StringComparison.Ordinal))
        {
            results.Add(view);
        }

        if (view is not ViewGroup viewGroup)
        {
            return;
        }

        for (var index = 0; index < viewGroup.ChildCount; index++)
        {
            var child = viewGroup.GetChildAt(index);
            if (child != null)
            {
                CollectFragmentContainerViews(child, results);
            }
        }
    }

    private static object? TryFindFragmentById(object fragmentManager, int viewId)
    {
        if (viewId <= 0)
        {
            return null;
        }

        try
        {
            return fragmentManager.GetType().GetMethod("FindFragmentById", new[] { typeof(int) })?.Invoke(fragmentManager, new object[] { viewId });
        }
        catch
        {
            return null;
        }
    }

    private static void CollectSurfaceBackedViews(
        View view,
        List<View> results,
        HashSet<nint> overlaidSurfaceHandles,
        bool includeTextureViews)
    {
        if (!IsVisibleForCapture(view))
        {
            return;
        }

        if ((view is SurfaceView || (includeTextureViews && view is TextureView)) &&
            overlaidSurfaceHandles.Add(view.Handle))
        {
            results.Add(view);
        }

        if (view is not ViewGroup viewGroup)
        {
            return;
        }

        for (var index = 0; index < viewGroup.ChildCount; index++)
        {
            var child = viewGroup.GetChildAt(index);
            if (child != null)
            {
                CollectSurfaceBackedViews(child, results, overlaidSurfaceHandles, includeTextureViews);
            }
        }
    }

    [SupportedOSPlatform("android26.0")]
    private static async Task OverlaySurfaceViewAsync(
        Canvas canvas,
        SurfaceView surfaceView,
        AndroidViewLocation rootLocation,
        float scaleX,
        float scaleY,
        CancellationToken cancellationToken)
    {
        if (!IsVisibleForCapture(surfaceView) ||
            surfaceView.Holder?.Surface == null ||
            !surfaceView.Holder.Surface.IsValid)
        {
            return;
        }

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        using var bitmap = Bitmap.CreateBitmap(surfaceView.Width, surfaceView.Height, Bitmap.Config.Argb8888!);

        try
        {
            PixelCopy.Request(
                surfaceView,
                bitmap,
                new PixelCopyFinishedListener(completion),
                PixelCopyThread.GetHandler());
        }
        catch
        {
            return;
        }

        if (await completion.Task != (int)PixelCopyResult.Success)
        {
            return;
        }

        DrawOverlayBitmap(canvas, surfaceView, bitmap, rootLocation, scaleX, scaleY);
    }

    private static void OverlayTextureView(
        Canvas canvas,
        TextureView textureView,
        AndroidViewLocation rootLocation,
        float scaleX,
        float scaleY)
    {
        if (!IsVisibleForCapture(textureView) || !textureView.IsAvailable)
        {
            return;
        }

        using var bitmap = textureView.Bitmap;
        if (bitmap == null)
        {
            return;
        }

        DrawOverlayBitmap(canvas, textureView, bitmap, rootLocation, scaleX, scaleY);
    }

    private static void DrawOverlayBitmap(
        Canvas canvas,
        View view,
        Bitmap bitmap,
        AndroidViewLocation rootLocation,
        float scaleX,
        float scaleY)
    {
        var location = GetViewLocationOnScreen(view);
        var left = (location.X - rootLocation.X) * scaleX;
        var top = (location.Y - rootLocation.Y) * scaleY;
        var destination = new RectF(
            left,
            top,
            left + (view.Width * scaleX),
            top + (view.Height * scaleY));
        canvas.DrawBitmap(bitmap, null, destination, null);
    }

    private static AndroidViewLocation GetViewLocationOnScreen(View view)
    {
        var location = new int[2];
        view.GetLocationOnScreen(location);
        return new AndroidViewLocation(location[0], location[1]);
    }

    private static void Clear(Canvas canvas)
    {
        canvas.DrawColor(Color.Transparent, PorterDuff.Mode.Clear!);
    }

    private readonly record struct AndroidViewLocation(int X, int Y);

    private sealed class PixelCopyFinishedListener : Java.Lang.Object, PixelCopy.IOnPixelCopyFinishedListener
    {
        private readonly TaskCompletionSource<int> completion;

        public PixelCopyFinishedListener(TaskCompletionSource<int> completion)
        {
            this.completion = completion;
        }

        public void OnPixelCopyFinished(int copyResult)
        {
            completion.TrySetResult(copyResult);
        }
    }

    private static class PixelCopyThread
    {
        private static readonly Lock sync = new();
        private static HandlerThread? handlerThread;
        private static Handler? handler;

        internal static Handler GetHandler()
        {
            lock (sync)
            {
                if (handlerThread == null || !handlerThread.IsAlive)
                {
                    handler?.Dispose();
                    handlerThread?.Dispose();
                    handlerThread = new HandlerThread("AnsightAndroidScenePixelCopy");
                    handlerThread.Start();
                    handler = new Handler(handlerThread.Looper!);
                }

                return handler!;
            }
        }
    }

    private sealed class AndroidActivityTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
    {
        private static readonly object sync = new();
        private static AndroidActivityTracker? instance;
        private Activity? currentActivity;

        internal static Activity? GetCurrentActivity()
        {
            EnsureRegistered();
            lock (sync)
            {
                if (instance?.currentActivity is { } currentActivity)
                {
                    return currentActivity;
                }

                var resolvedActivity = TryResolveCurrentActivity();
                if (resolvedActivity != null && instance != null)
                {
                    instance.currentActivity = resolvedActivity;
                }

                return resolvedActivity;
            }
        }

        internal static void SetCurrentActivity(Activity activity)
        {
            ArgumentNullException.ThrowIfNull(activity);
            EnsureRegistered();
            lock (sync)
            {
                if (instance != null)
                {
                    instance.currentActivity = activity;
                }
            }
        }

        private static void EnsureRegistered()
        {
            if (instance != null)
            {
                return;
            }

            lock (sync)
            {
                if (instance != null)
                {
                    return;
                }

                if (Application.Context is not Application application)
                {
                    return;
                }

                instance = new AndroidActivityTracker();
                application.RegisterActivityLifecycleCallbacks(instance);
            }
        }

        private static Activity? TryResolveCurrentActivity()
        {
            foreach (var assemblyName in new[] { "Microsoft.Maui.Essentials", "Microsoft.Maui" })
            {
                try
                {
                    var platformType = Type.GetType($"Microsoft.Maui.ApplicationModel.Platform, {assemblyName}", throwOnError: false);
                    var currentActivityProperty = platformType?.GetProperty(
                        "CurrentActivity",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (currentActivityProperty?.GetValue(null) is Activity activity)
                    {
                        return activity;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
        {
            lock (sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivityDestroyed(Activity activity)
        {
            lock (sync)
            {
                if (ReferenceEquals(currentActivity, activity))
                {
                    currentActivity = null;
                }
            }
        }

        public void OnActivityPaused(Activity activity)
        {
        }

        public void OnActivityResumed(Activity activity)
        {
            lock (sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
        {
        }

        public void OnActivityStarted(Activity activity)
        {
            lock (sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivityStopped(Activity activity)
        {
        }
    }
}
#endif
