#if ANDROID
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Views;
using System.Net.WebSockets;
using Ansight.Pairing;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private static readonly Android.OS.Handler MainHandler = new(Android.OS.Looper.MainLooper!);
    private static readonly Lock captureStateGate = new();
    private static CaptureBitmapState? reusableCaptureState;

    private static partial Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync<ISessionJpegCaptureSurface?>(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await CaptureSurfaceBitmapAsync(options, cancellationToken);
        });
    }

    private static partial Task<OperationResult> SendSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        return surface is SessionJpegCaptureSurface androidSurface
            ? SendSurfaceAsync(androidSurface, options, transport, cancellationToken)
            : Task.FromResult(OperationResult.FromFailure("Session JPEG capture surface type mismatch."));
    }

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<T?> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainHandler.Post(() =>
        {
            try
            {
                taskCompletionSource.SetResult(capture());
            }
            catch (Exception ex)
            {
                taskCompletionSource.SetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<Task<T?>> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainHandler.Post(async () =>
        {
            try
            {
                taskCompletionSource.SetResult(await capture());
            }
            catch (Exception ex)
            {
                taskCompletionSource.SetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    private static async Task<SessionJpegCaptureSurface?> CaptureSurfaceBitmapAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            return null;
        }

        var targetWidth = ResolveTargetWidth(rootView.Width, options.MaxWidth);
        var targetHeight = ResolveScaledHeight(rootView.Width, rootView.Height, targetWidth);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return null;
        }

        var captureState = AcquireCaptureState(targetWidth, targetHeight);
        try
        {
            captureState.Clear();
            if (!await TryCaptureSceneAsync(activity!, rootView, captureState.Bitmap, captureState.Canvas, targetWidth, targetHeight, cancellationToken))
            {
                var saveCount = captureState.Canvas.Save();
                try
                {
                    if (targetWidth != rootView.Width || targetHeight != rootView.Height)
                    {
                        captureState.Canvas.Scale(targetWidth / (float)rootView.Width, targetHeight / (float)rootView.Height);
                    }

                    rootView.Draw(captureState.Canvas);
                }
                finally
                {
                    captureState.Canvas.RestoreToCount(saveCount);
                }
            }

            return new SessionJpegCaptureSurface(captureState, DateTimeOffset.UtcNow, targetWidth, targetHeight);
        }
        catch
        {
            captureState.Release();
            throw;
        }
    }

    private static async Task<bool> TryCaptureSceneAsync(
        Activity activity,
        View rootView,
        Bitmap bitmap,
        Canvas canvas,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        var capturedActivityWindow = await TryCaptureActivityWindowAsync(activity, bitmap, cancellationToken);
        var topLevelViews = GetTopLevelViews(activity);
        if (topLevelViews.Count == 0)
        {
            return capturedActivityWindow;
        }

        var scaleX = targetWidth / (float)rootView.Width;
        var scaleY = targetHeight / (float)rootView.Height;
        var overlaidSurfaceHandles = new HashSet<nint>();

        foreach (var topLevelView in topLevelViews)
        {
            var shouldDrawTopLevelView = !(capturedActivityWindow && topLevelView.Handle == rootView.Handle);
            if (shouldDrawTopLevelView)
            {
                DrawTopLevelView(canvas, topLevelView, scaleX, scaleY);
            }

            await OverlaySurfaceBackedChildrenAsync(canvas, topLevelView, scaleX, scaleY, overlaidSurfaceHandles, cancellationToken);
        }

        await OverlayFragmentHostedSurfaceBackedViewsAsync(activity, canvas, scaleX, scaleY, overlaidSurfaceHandles, cancellationToken);
        return true;
    }

    private static async Task<bool> TryCaptureActivityWindowAsync(Activity activity, Bitmap bitmap, CancellationToken cancellationToken)
    {
        if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.O || activity.Window == null)
        {
            return false;
        }

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            PixelCopy.Request(
                activity.Window,
                bitmap,
                new PixelCopyFinishedListener(completion),
                PixelCopyThread.GetHandler());
        }
        catch
        {
            return false;
        }

        return await completion.Task == (int)PixelCopyResult.Success;
    }

    private static List<View> GetTopLevelViews(Activity activity)
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
                                if (views == null)
                                {
                                    return topLevelViews;
                                }

                                foreach (var view in views)
                                {
                                    if (view != null && ShouldCaptureTopLevelView(view, activity, packageName))
                                    {
                                        topLevelViews.Add(view);
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
                if (!IsSameJavaObject(topLevelView, activityRootView))
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

    private static void DrawTopLevelView(Canvas canvas, View topLevelView, float scaleX, float scaleY)
    {
        var location = new int[2];
        topLevelView.GetLocationOnScreen(location);

        var saveCount = canvas.Save();
        try
        {
            canvas.Scale(scaleX, scaleY);
            canvas.Translate(location[0], location[1]);
            topLevelView.Draw(canvas);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static async Task OverlaySurfaceBackedChildrenAsync(
        Canvas canvas,
        View rootView,
        float scaleX,
        float scaleY,
        HashSet<nint> overlaidSurfaceHandles,
        CancellationToken cancellationToken)
    {
        var specialViews = new List<View>();
        CollectSurfaceBackedViews(rootView, specialViews, overlaidSurfaceHandles);
        foreach (var specialView in specialViews)
        {
            switch (specialView)
            {
                case SurfaceView surfaceView:
                    await OverlaySurfaceViewAsync(canvas, surfaceView, scaleX, scaleY, cancellationToken);
                    break;
                case TextureView textureView:
                    OverlayTextureView(canvas, textureView, scaleX, scaleY);
                    break;
            }
        }
    }

    private static async Task OverlayFragmentHostedSurfaceBackedViewsAsync(
        Activity activity,
        Canvas canvas,
        float scaleX,
        float scaleY,
        HashSet<nint> overlaidSurfaceHandles,
        CancellationToken cancellationToken)
    {
        foreach (var fragmentView in GetFragmentRootViews(activity))
        {
            await OverlaySurfaceBackedChildrenAsync(
                canvas,
                fragmentView,
                scaleX,
                scaleY,
                overlaidSurfaceHandles,
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

    private static void CollectSurfaceBackedViews(View view, List<View> results, HashSet<nint> overlaidSurfaceHandles)
    {
        if (!IsVisibleForCapture(view))
        {
            return;
        }

        if ((view is SurfaceView || view is TextureView) &&
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
                CollectSurfaceBackedViews(child, results, overlaidSurfaceHandles);
            }
        }
    }

    private static async Task OverlaySurfaceViewAsync(Canvas canvas, SurfaceView surfaceView, float scaleX, float scaleY, CancellationToken cancellationToken)
    {
        if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.O ||
            !IsVisibleForCapture(surfaceView) ||
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

        DrawOverlayBitmap(canvas, surfaceView, bitmap, scaleX, scaleY);
    }

    private static void OverlayTextureView(Canvas canvas, TextureView textureView, float scaleX, float scaleY)
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

        DrawOverlayBitmap(canvas, textureView, bitmap, scaleX, scaleY);
    }

    private static void DrawOverlayBitmap(Canvas canvas, View view, Bitmap bitmap, float scaleX, float scaleY)
    {
        var location = new int[2];
        view.GetLocationOnScreen(location);
        var destination = new RectF(
            location[0] * scaleX,
            location[1] * scaleY,
            (location[0] + view.Width) * scaleX,
            (location[1] + view.Height) * scaleY);
        canvas.DrawBitmap(bitmap, null, destination, null);
    }

    private static async Task<OperationResult> SendSurfaceAsync(
        SessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        using var frame = EncodeSurface(surface, options);
        if (frame is null)
        {
            return OperationResult.FromSuccess("Session JPEG frame skipped.");
        }

        return await transport.SendBinaryAsync(frame.Payload, WebSocketMessageType.Binary, cancellationToken);
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        using var stream = new PooledBufferStream(
            SessionJpegWireProtocol.HeaderSize + EstimateInitialJpegByteCapacity(surface.Width, surface.Height));
        stream.ReservePrefix(SessionJpegWireProtocol.HeaderSize);
        if (!surface.Bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, options.Quality, stream))
        {
            return null;
        }

        var jpegLength = stream.LengthWritten - SessionJpegWireProtocol.HeaderSize;
        SessionJpegWireProtocol.WriteHeader(
            stream.GetWrittenSpan(SessionJpegWireProtocol.HeaderSize),
            surface.CapturedAtUtc,
            surface.Width,
            surface.Height,
            options.Quality,
            jpegLength);

        RecordEncodedJpegByteCount(jpegLength);
        return stream.DetachFrame();
    }

    private static CaptureBitmapState AcquireCaptureState(int width, int height)
    {
        lock (captureStateGate)
        {
            if (reusableCaptureState is not null)
            {
                if (reusableCaptureState.Matches(width, height) && !reusableCaptureState.IsInUse)
                {
                    reusableCaptureState.Acquire();
                    return reusableCaptureState;
                }

                if (!reusableCaptureState.Matches(width, height) && !reusableCaptureState.IsInUse)
                {
                    reusableCaptureState.Dispose();
                    reusableCaptureState = CaptureBitmapState.CreateReusable(width, height);
                    reusableCaptureState.Acquire();
                    return reusableCaptureState;
                }
            }

            if (reusableCaptureState is null)
            {
                reusableCaptureState = CaptureBitmapState.CreateReusable(width, height);
                reusableCaptureState.Acquire();
                return reusableCaptureState;
            }

            var temporaryState = CaptureBitmapState.CreateTemporary(width, height);
            temporaryState.Acquire();
            return temporaryState;
        }
    }

    private sealed class SessionJpegCaptureSurface : ISessionJpegCaptureSurface
    {
        private readonly CaptureBitmapState captureState;

        public SessionJpegCaptureSurface(CaptureBitmapState captureState, DateTimeOffset capturedAtUtc, int width, int height)
        {
            this.captureState = captureState;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
        }

        public Android.Graphics.Bitmap Bitmap => captureState.Bitmap;

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            captureState.Release();
        }
    }

    private sealed class CaptureBitmapState : IDisposable
    {
        private bool inUse;

        private CaptureBitmapState(int width, int height, bool reusable)
        {
            Width = width;
            Height = height;
            IsReusable = reusable;
            Bitmap = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888!);
            Canvas = new Android.Graphics.Canvas(Bitmap);
        }

        public Android.Graphics.Bitmap Bitmap { get; }

        public Android.Graphics.Canvas Canvas { get; }

        public int Width { get; }

        public int Height { get; }

        public bool IsReusable { get; }

        public bool IsInUse => inUse;

        public static CaptureBitmapState CreateReusable(int width, int height) => new(width, height, reusable: true);

        public static CaptureBitmapState CreateTemporary(int width, int height) => new(width, height, reusable: false);

        public void Acquire()
        {
            if (inUse)
            {
                throw new InvalidOperationException("Capture bitmap state is already in use.");
            }

            inUse = true;
        }

        public void Clear()
        {
            Canvas.DrawColor(Android.Graphics.Color.Transparent, Android.Graphics.PorterDuff.Mode.Clear!);
        }

        public bool Matches(int width, int height) => Width == width && Height == height;

        public void Release()
        {
            if (IsReusable)
            {
                lock (captureStateGate)
                {
                    inUse = false;
                }

                return;
            }

            Dispose();
        }

        public void Dispose()
        {
            Canvas.Dispose();
            Bitmap.Dispose();
        }
    }

    private sealed class PixelCopyFinishedListener : Java.Lang.Object, Android.Views.PixelCopy.IOnPixelCopyFinishedListener
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
        private static Android.OS.HandlerThread? handlerThread;
        private static Android.OS.Handler? handler;

        internal static Android.OS.Handler GetHandler()
        {
            lock (sync)
            {
                if (handlerThread == null || !handlerThread.IsAlive)
                {
                    handler?.Dispose();
                    handlerThread?.Dispose();
                    handlerThread = new Android.OS.HandlerThread("AnsightSessionPixelCopy");
                    handlerThread.Start();
                    handler = new Android.OS.Handler(handlerThread.Looper!);
                }

                return handler!;
            }
        }
    }

    private sealed class AndroidActivityTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
    {
        private static readonly object Sync = new();
        private static AndroidActivityTracker? instance;
        private Activity? currentActivity;

        internal static Activity? GetCurrentActivity()
        {
            EnsureRegistered();
            lock (Sync)
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

        private static void EnsureRegistered()
        {
            if (instance is not null)
            {
                return;
            }

            lock (Sync)
            {
                if (instance is not null)
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

        public void OnActivityCreated(Activity activity, Android.OS.Bundle? savedInstanceState)
        {
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivityDestroyed(Activity activity)
        {
            lock (Sync)
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
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivitySaveInstanceState(Activity activity, Android.OS.Bundle outState)
        {
        }

        public void OnActivityStarted(Activity activity)
        {
            lock (Sync)
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
