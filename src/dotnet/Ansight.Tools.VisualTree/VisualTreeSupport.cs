namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

#if ANDROID
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.OS;
using Android.Views;
using Android.Widget;
#elif IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;
#endif

internal static class VisualTreeSupport
{
    private static int lastEncodedScreenshotBytes = 32 * 1024;

    internal static Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
    {
        var includeBounds = GetBoolean(arguments, "includeBounds", defaultValue: true);
        var includeProperties = GetBoolean(arguments, "includeComputedStyles", defaultValue: false);
        var maxDepth = GetInt(arguments, "maxDepth", defaultValue: 8, minimum: 1, maximum: 64);
        var rootNodeId = GetString(arguments, "rootNodeId");

        return RunOnUiThreadAsync(() =>
        {
            if (!TryCaptureTree(includeProperties, out var rootNode, out var error))
            {
                return ToolResult.Failure(error ?? "Unable to capture the current visual tree.", errorCode: "visual_tree_unavailable");
            }

            var selectedRoot = string.IsNullOrWhiteSpace(rootNodeId) ? rootNode : rootNode!.Find(rootNodeId!);
            if (selectedRoot == null)
            {
                return ToolResult.Failure($"The node '{rootNodeId}' was not found.", errorCode: "visual_tree_node_not_found");
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["root"] = selectedRoot.ToJson(includeBounds, includeProperties, maxDepth)
            };

            return ToolResult.Success(payload);
        });
    }

    internal static Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
    {
        var nodeId = GetRequiredString(arguments, "nodeId");
        var includeAncestors = GetBoolean(arguments, "includeAncestors", defaultValue: false);
        var includeDescendants = GetBoolean(arguments, "includeDescendants", defaultValue: false);
        var includeProperties = GetBoolean(arguments, "includeProperties", defaultValue: true);

        return RunOnUiThreadAsync(() =>
        {
            if (!TryCaptureTree(includeProperties, out var rootNode, out var error))
            {
                return ToolResult.Failure(error ?? "Unable to inspect the current visual tree.", errorCode: "visual_tree_unavailable");
            }

            var ancestors = new List<VisualNode>();
            var node = rootNode!.Find(nodeId, ancestors);
            if (node == null)
            {
                return ToolResult.Failure($"The node '{nodeId}' was not found.", errorCode: "visual_tree_node_not_found");
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = node.ToJson(includeBounds: true, includeProperties, maxDepth: 32)
            };

            if (includeAncestors)
            {
                var ancestorNodes = new JsonArray();
                foreach (var ancestor in ancestors)
                {
                    ancestorNodes.Add(ancestor.ToJson(includeBounds: true, includeProperties, maxDepth: 0));
                }

                payload["ancestors"] = ancestorNodes;
            }

            if (includeDescendants)
            {
                var descendantNodes = new JsonArray();
                foreach (var descendant in node.Descendants())
                {
                    descendantNodes.Add(descendant.ToJson(includeBounds: true, includeProperties, maxDepth: 32));
                }

                payload["descendants"] = descendantNodes;
            }

            return ToolResult.Success(payload);
        });
    }

    internal static Task<ToolResult> GetScreenshotAsync(IReadOnlyDictionary<string, string> arguments)
    {
        var format = (GetString(arguments, "format") ?? "png").Trim().ToLowerInvariant();
        var quality = GetInt(arguments, "quality", defaultValue: 90, minimum: 1, maximum: 100);
        var maxWidth = GetOptionalInt(arguments, "maxWidth", minimum: 1, maximum: 8192);
        var annotateNodeIds = GetBoolean(arguments, "annotateNodeIds", defaultValue: false);

#if ANDROID
        return RunOnUiThreadAsync(async () =>
        {
            var screenshotResult = await CaptureScreenshotAsync(format, quality, maxWidth, annotateNodeIds);
            if (screenshotResult.Screenshot == null)
            {
                return ToolResult.Failure(screenshotResult.Error ?? "Unable to capture a screenshot.", errorCode: "visual_screenshot_failed");
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["format"] = screenshotResult.Screenshot.Format,
                ["width"] = screenshotResult.Screenshot.Width,
                ["height"] = screenshotResult.Screenshot.Height,
                ["base64"] = screenshotResult.Screenshot.Base64,
                ["annotationApplied"] = screenshotResult.Screenshot.AnnotationApplied
            };

            return ToolResult.Success(payload);
        });
#else
        return RunOnUiThreadAsync(() =>
        {
            if (!TryCaptureScreenshot(format, quality, maxWidth, annotateNodeIds, out var screenshot, out var error))
            {
                return ToolResult.Failure(error ?? "Unable to capture a screenshot.", errorCode: "visual_screenshot_failed");
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["format"] = screenshot!.Format,
                ["width"] = screenshot.Width,
                ["height"] = screenshot.Height,
                ["base64"] = screenshot.Base64,
                ["annotationApplied"] = screenshot.AnnotationApplied
            };

            return ToolResult.Success(payload);
        });
#endif
    }

    private static string CurrentPlatform
    {
        get
        {
#if ANDROID
            return "android";
#elif IOS
            return "ios";
#elif MACCATALYST
            return "maccatalyst";
#else
            return "unknown";
#endif
        }
    }

    private static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int defaultValue, int minimum, int maximum)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsedValue, minimum, maximum);
    }

    private static int? GetOptionalInt(IReadOnlyDictionary<string, string> arguments, string key, int minimum, int maximum)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsedValue, minimum, maximum);
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, string> arguments, string key, bool defaultValue)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (bool.TryParse(rawValue, out var boolValue))
        {
            return boolValue;
        }

        return rawValue switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException($"The argument '{key}' must be a boolean.")
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string GetRequiredString(IReadOnlyDictionary<string, string> arguments, string key)
        => GetString(arguments, key) ?? throw new InvalidOperationException($"The argument '{key}' is required.");

    private sealed class VisualNode
    {
        internal VisualNode(
            string id,
            string type,
            string? label,
            bool isVisible,
            bool isEnabled,
            bool isFocusable,
            JsonObject? bounds,
            JsonObject? properties,
            List<VisualNode> children)
        {
            Id = id;
            Type = type;
            Label = label;
            IsVisible = isVisible;
            IsEnabled = isEnabled;
            IsFocusable = isFocusable;
            Bounds = bounds;
            Properties = properties;
            Children = children;
        }

        internal string Id { get; }
        internal string Type { get; }
        internal string? Label { get; }
        internal bool IsVisible { get; }
        internal bool IsEnabled { get; }
        internal bool IsFocusable { get; }
        internal JsonObject? Bounds { get; }
        internal JsonObject? Properties { get; }
        internal List<VisualNode> Children { get; }

        internal VisualNode? Find(string nodeId)
        {
            if (string.Equals(Id, nodeId, StringComparison.Ordinal))
            {
                return this;
            }

            foreach (var child in Children)
            {
                var match = child.Find(nodeId);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        internal VisualNode? Find(string nodeId, List<VisualNode> ancestors)
        {
            if (string.Equals(Id, nodeId, StringComparison.Ordinal))
            {
                return this;
            }

            foreach (var child in Children)
            {
                ancestors.Add(this);
                var match = child.Find(nodeId, ancestors);
                if (match != null)
                {
                    return match;
                }

                ancestors.RemoveAt(ancestors.Count - 1);
            }

            return null;
        }

        internal IEnumerable<VisualNode> Descendants()
        {
            foreach (var child in Children)
            {
                yield return child;

                foreach (var descendant in child.Descendants())
                {
                    yield return descendant;
                }
            }
        }

        internal JsonObject ToJson(bool includeBounds, bool includeProperties, int maxDepth)
        {
            var json = new JsonObject
            {
                ["id"] = Id,
                ["type"] = Type,
                ["label"] = Label,
                ["visible"] = IsVisible,
                ["enabled"] = IsEnabled,
                ["focusable"] = IsFocusable,
                ["childCount"] = Children.Count
            };

            if (includeBounds && Bounds != null)
            {
                json["bounds"] = Bounds.DeepClone();
            }

            if (includeProperties && Properties != null && Properties.Count > 0)
            {
                json["properties"] = Properties.DeepClone();
            }

            if (maxDepth > 0)
            {
                var children = new JsonArray();
                foreach (var child in Children)
                {
                    children.Add(child.ToJson(includeBounds, includeProperties, maxDepth - 1));
                }

                json["children"] = children;
            }

            return json;
        }
    }

    private sealed record ScreenshotCapture(string Format, int Width, int Height, string Base64, bool AnnotationApplied);

#if ANDROID
    private static Task<ToolResult> RunOnUiThreadAsync(Func<ToolResult> action)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        if (activity == null)
        {
            return Task.FromResult(ToolResult.Failure("No foreground Android activity is available.", errorCode: "visual_tree_no_activity"));
        }

        var completion = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetResult(ToolResult.Failure(exception.Message, errorCode: "visual_tree_execution_failed"));
            }
        });

        return completion.Task;
    }

    private static Task<ToolResult> RunOnUiThreadAsync(Func<Task<ToolResult>> action)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        if (activity == null)
        {
            return Task.FromResult(ToolResult.Failure("No foreground Android activity is available.", errorCode: "visual_tree_no_activity"));
        }

        var completion = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception exception)
            {
                completion.TrySetResult(ToolResult.Failure(exception.Message, errorCode: "visual_tree_execution_failed"));
            }
        });

        return completion.Task;
    }

    private static bool TryCaptureTree(bool includeProperties, out VisualNode? rootNode, out string? error)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null)
        {
            rootNode = null;
            error = "No Android root view is currently available.";
            return false;
        }

        rootNode = BuildAndroidNode(rootView, rootView, includeProperties);
        error = null;
        return true;
    }

    private static VisualNode BuildAndroidNode(View view, View rootView, bool includeProperties)
    {
        var location = new int[2];
        view.GetLocationOnScreen(location);

        var properties = includeProperties
            ? new JsonObject
            {
                ["alpha"] = view.Alpha,
                ["clickable"] = view.Clickable,
                ["selected"] = view.Selected,
                ["activated"] = view.Activated,
                ["contentDescription"] = view.ContentDescription?.ToString()
            }
            : null;

        var children = new List<VisualNode>();
        if (view is ViewGroup group)
        {
            for (var index = 0; index < group.ChildCount; index++)
            {
                var child = group.GetChildAt(index);
                if (child != null)
                {
                    children.Add(BuildAndroidNode(child, rootView, includeProperties));
                }
            }
        }

        return new VisualNode(
            id: view.Handle != IntPtr.Zero ? view.Handle.ToInt64().ToString() : view.GetHashCode().ToString(),
            type: view.Class?.SimpleName ?? view.GetType().Name,
            label: GetAndroidLabel(view),
            isVisible: view.Visibility == ViewStates.Visible && view.Alpha > 0,
            isEnabled: view.Enabled,
            isFocusable: view.Focusable,
            bounds: new JsonObject
            {
                ["x"] = location[0],
                ["y"] = location[1],
                ["width"] = view.Width,
                ["height"] = view.Height
            },
            properties: properties,
            children: children);
    }

    private static string? GetAndroidLabel(View view)
    {
        return view switch
        {
            TextView textView when !string.IsNullOrWhiteSpace(textView.Text) => textView.Text,
            _ => view.ContentDescription?.ToString()
        };
    }

    private static async Task<AndroidScreenshotCaptureResult> CaptureScreenshotAsync(string format, int quality, int? maxWidth, bool annotateNodeIds)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            return new AndroidScreenshotCaptureResult(null, "No Android root view is available for screenshot capture.");
        }

        using var bitmap = Bitmap.CreateBitmap(rootView.Width, rootView.Height, Bitmap.Config.Argb8888!);
        using (var canvas = new Canvas(bitmap))
        {
            if (!await TryCaptureSceneAsync(activity!, rootView, bitmap, canvas))
            {
                rootView.Draw(canvas);
            }
        }

        Bitmap workingBitmap = bitmap;
        Bitmap? scaledBitmap = null;
        if (maxWidth.HasValue && bitmap.Width > maxWidth.Value)
        {
            var scaledHeight = (int)Math.Round(bitmap.Height * (maxWidth.Value / (double)bitmap.Width));
            scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, maxWidth.Value, scaledHeight, filter: true);
            workingBitmap = scaledBitmap;
        }

        try
        {
            using var stream = new MemoryStream(Math.Max(EstimateInitialEncodedScreenshotCapacity(workingBitmap.Width, workingBitmap.Height), 1024));
            var success = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
                ? workingBitmap.Compress(Bitmap.CompressFormat.Jpeg!, quality, stream)
                : workingBitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);

            if (!success)
            {
                return new AndroidScreenshotCaptureResult(null, "Android bitmap compression failed.");
            }

            var encodedLength = checked((int)stream.Length);
            ReportEncodedScreenshotBytes(encodedLength);
            return new AndroidScreenshotCaptureResult(
                new ScreenshotCapture(
                Format: string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpeg" : "png",
                Width: workingBitmap.Width,
                Height: workingBitmap.Height,
                Base64: ToBase64String(stream, encodedLength),
                AnnotationApplied: false && annotateNodeIds),
                null);
        }
        finally
        {
            scaledBitmap?.Dispose();
        }
    }

    private static async Task<bool> TryCaptureSceneAsync(Activity activity, View rootView, Bitmap bitmap, Canvas canvas)
    {
        var capturedActivityWindow = await TryCaptureActivityWindowAsync(activity, bitmap);
        var topLevelViews = GetTopLevelViews(activity);
        if (topLevelViews.Count == 0)
        {
            return capturedActivityWindow;
        }

        var overlaidSurfaceHandles = new HashSet<nint>();
        foreach (var topLevelView in topLevelViews)
        {
            var shouldDrawTopLevelView = !(capturedActivityWindow && topLevelView.Handle == rootView.Handle);
            if (shouldDrawTopLevelView)
            {
                DrawTopLevelView(canvas, topLevelView);
            }

            await OverlaySurfaceBackedChildrenAsync(canvas, topLevelView, overlaidSurfaceHandles);
        }

        await OverlayFragmentHostedSurfaceBackedViewsAsync(activity, canvas, overlaidSurfaceHandles);
        return true;
    }

    private static async Task<bool> TryCaptureActivityWindowAsync(Activity activity, Bitmap bitmap)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O || activity.Window == null)
        {
            return false;
        }

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private static void DrawTopLevelView(Canvas canvas, View topLevelView)
    {
        var location = new int[2];
        topLevelView.GetLocationOnScreen(location);

        var saveCount = canvas.Save();
        try
        {
            canvas.Translate(location[0], location[1]);
            topLevelView.Draw(canvas);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static async Task OverlaySurfaceBackedChildrenAsync(Canvas canvas, View rootView, HashSet<nint> overlaidSurfaceHandles)
    {
        var specialViews = new List<View>();
        CollectSurfaceBackedViews(rootView, specialViews, overlaidSurfaceHandles);
        foreach (var specialView in specialViews)
        {
            switch (specialView)
            {
                case SurfaceView surfaceView:
                    await OverlaySurfaceViewAsync(canvas, surfaceView);
                    break;
                case TextureView textureView:
                    OverlayTextureView(canvas, textureView);
                    break;
            }
        }
    }

    private static async Task OverlayFragmentHostedSurfaceBackedViewsAsync(Activity activity, Canvas canvas, HashSet<nint> overlaidSurfaceHandles)
    {
        foreach (var fragmentView in GetFragmentRootViews(activity))
        {
            await OverlaySurfaceBackedChildrenAsync(canvas, fragmentView, overlaidSurfaceHandles);
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

    private static async Task OverlaySurfaceViewAsync(Canvas canvas, SurfaceView surfaceView)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O ||
            !IsVisibleForCapture(surfaceView) ||
            surfaceView.Holder?.Surface == null ||
            !surfaceView.Holder.Surface.IsValid)
        {
            return;
        }

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        DrawOverlayBitmap(canvas, surfaceView, bitmap);
    }

    private static void OverlayTextureView(Canvas canvas, TextureView textureView)
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

        DrawOverlayBitmap(canvas, textureView, bitmap);
    }

    private static void DrawOverlayBitmap(Canvas canvas, View view, Bitmap bitmap)
    {
        var location = new int[2];
        view.GetLocationOnScreen(location);
        var destination = new RectF(
            location[0],
            location[1],
            location[0] + view.Width,
            location[1] + view.Height);
        canvas.DrawBitmap(bitmap, null, destination, null);
    }

    private sealed record AndroidScreenshotCaptureResult(ScreenshotCapture? Screenshot, string? Error);

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
                    handlerThread = new HandlerThread("AnsightVisualTreePixelCopy");
                    handlerThread.Start();
                    handler = new Handler(handlerThread.Looper!);
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
            if (instance != null)
            {
                return;
            }

            lock (Sync)
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

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
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

        public void OnActivityPaused(Activity activity) { }
        public void OnActivityResumed(Activity activity)
        {
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
        public void OnActivityStarted(Activity activity)
        {
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivityStopped(Activity activity) { }
    }
#elif IOS || MACCATALYST
    private static Task<ToolResult> RunOnUiThreadAsync(Func<ToolResult> action)
    {
        var completion = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            try
            {
                using var autoreleasePool = new NSAutoreleasePool();
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetResult(ToolResult.Failure(exception.Message, errorCode: "visual_tree_execution_failed"));
            }
        });

        return completion.Task;
    }

    private static bool TryCaptureTree(bool includeProperties, out VisualNode? rootNode, out string? error)
    {
        var window = GetActiveWindow();
        var rootView = window?.RootViewController?.View ?? window;
        if (rootView == null || window == null)
        {
            rootNode = null;
            error = "No active UIWindow is available.";
            return false;
        }

        rootNode = BuildAppleNode(rootView, window, includeProperties);
        error = null;
        return true;
    }

    private static VisualNode BuildAppleNode(UIView view, UIWindow window, bool includeProperties)
    {
        var frame = view.ConvertRectToView(view.Bounds, window);
        var properties = includeProperties
            ? new JsonObject
            {
                ["alpha"] = (double)view.Alpha,
                ["opaque"] = view.Opaque,
                ["clipsToBounds"] = view.ClipsToBounds,
                ["userInteractionEnabled"] = view.UserInteractionEnabled,
                ["accessibilityIdentifier"] = view.AccessibilityIdentifier
            }
            : null;

        var children = new List<VisualNode>(view.Subviews.Length);
        foreach (var child in view.Subviews)
        {
            children.Add(BuildAppleNode(child, window, includeProperties));
        }

        return new VisualNode(
            id: !string.IsNullOrWhiteSpace(view.Handle.ToString()) ? view.Handle.ToString() : view.GetHashCode().ToString(),
            type: view.GetType().Name,
            label: GetAppleLabel(view),
            isVisible: !view.Hidden && view.Alpha > 0,
            isEnabled: view.UserInteractionEnabled,
            isFocusable: view.CanBecomeFocused,
            bounds: new JsonObject
            {
                ["x"] = (double)frame.X,
                ["y"] = (double)frame.Y,
                ["width"] = (double)frame.Width,
                ["height"] = (double)frame.Height
            },
            properties: properties,
            children: children);
    }

    private static string? GetAppleLabel(UIView view)
    {
        return view switch
        {
            UILabel label when !string.IsNullOrWhiteSpace(label.Text) => label.Text,
            UIButton button when !string.IsNullOrWhiteSpace(button.CurrentTitle) => button.CurrentTitle,
            UITextField textField when !string.IsNullOrWhiteSpace(textField.Text) => textField.Text,
            UITextView textView when !string.IsNullOrWhiteSpace(textView.Text) => textView.Text,
            _ => view.AccessibilityLabel ?? view.AccessibilityIdentifier
        };
    }

    private static bool TryCaptureScreenshot(string format, int quality, int? maxWidth, bool annotateNodeIds, out ScreenshotCapture? screenshot, out string? error)
    {
        var window = GetActiveWindow();
        if (window == null)
        {
            screenshot = null;
            error = "No active UIWindow is available.";
            return false;
        }

        var originalBounds = window.Bounds;
        if (originalBounds.Width <= 0 || originalBounds.Height <= 0)
        {
            screenshot = null;
            error = "The active UIWindow has no drawable bounds.";
            return false;
        }

        var renderScale = GetRenderScale(window);
        var sourcePixelWidth = (int)Math.Round(originalBounds.Width * renderScale);
        var sourcePixelHeight = (int)Math.Round(originalBounds.Height * renderScale);
        var targetPixelWidth = sourcePixelWidth;
        if (maxWidth.HasValue && maxWidth.Value < sourcePixelWidth)
        {
            targetPixelWidth = maxWidth.Value;
        }

        var targetPixelHeight = targetPixelWidth >= sourcePixelWidth
            ? sourcePixelHeight
            : Math.Max(1, (int)Math.Round(sourcePixelHeight * (targetPixelWidth / (double)sourcePixelWidth)));

        var targetSize = new CGSize(targetPixelWidth, targetPixelHeight);

        var rendererFormat = UIGraphicsImageRendererFormat.DefaultFormat;
        rendererFormat.Opaque = false;
        rendererFormat.Scale = 1;

        using var renderer = new UIGraphicsImageRenderer(targetSize, rendererFormat);
        using var imageData = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
            ? renderer.CreateJpeg((nfloat)(quality / 100d), renderContext =>
            {
                renderContext.CGContext.ScaleCTM((nfloat)(targetSize.Width / originalBounds.Width), (nfloat)(targetSize.Height / originalBounds.Height));
                window.DrawViewHierarchy(originalBounds, afterScreenUpdates: true);
            })
            : renderer.CreatePng(renderContext =>
            {
                renderContext.CGContext.ScaleCTM((nfloat)(targetSize.Width / originalBounds.Width), (nfloat)(targetSize.Height / originalBounds.Height));
                window.DrawViewHierarchy(originalBounds, afterScreenUpdates: true);
            });

        if (imageData == null)
        {
            screenshot = null;
            error = "Failed to render or encode the current UIWindow.";
            return false;
        }

        ReportEncodedScreenshotBytes(checked((long)imageData.Length));
        screenshot = new ScreenshotCapture(
            Format: string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpeg" : "png",
            Width: targetPixelWidth,
            Height: targetPixelHeight,
            Base64: imageData.GetBase64EncodedString(NSDataBase64EncodingOptions.None) ?? string.Empty,
            AnnotationApplied: false && annotateNodeIds);
        error = null;
        return true;
    }

    private static UIWindow? GetActiveWindow()
    {
        var connectedScenes = UIApplication.SharedApplication.ConnectedScenes;
        foreach (var scene in connectedScenes)
        {
            if (scene is not UIWindowScene windowScene)
            {
                continue;
            }

            var activeWindow = windowScene.Windows.FirstOrDefault(window => window.IsKeyWindow)
                ?? windowScene.Windows.FirstOrDefault(window => !window.Hidden);
            if (activeWindow != null)
            {
                return activeWindow;
            }
        }

        return UIApplication.SharedApplication.Windows.FirstOrDefault(window => window.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows.FirstOrDefault(window => !window.Hidden);
    }

    private static nfloat GetRenderScale(UIWindow window)
    {
        var scale = window.Screen?.Scale ?? UIScreen.MainScreen.Scale;
        return scale > 0 ? scale : 1;
    }
#else
    private static Task<ToolResult> RunOnUiThreadAsync(Func<ToolResult> action) => Task.FromResult(ToolResult.Failure("Visual tree tools are only supported on Android, iOS, and Mac Catalyst.", errorCode: "visual_tree_platform_unsupported"));
    private static bool TryCaptureTree(bool includeProperties, out VisualNode? rootNode, out string? error)
    {
        rootNode = null;
        error = "Visual tree capture is not available on this platform.";
        return false;
    }

    private static bool TryCaptureScreenshot(string format, int quality, int? maxWidth, bool annotateNodeIds, out ScreenshotCapture? screenshot, out string? error)
    {
        screenshot = null;
        error = "Screenshot capture is not available on this platform.";
        return false;
    }
#endif

    private static int EstimateInitialEncodedScreenshotCapacity(int width, int height)
    {
        var lastEncodedBytes = Volatile.Read(ref lastEncodedScreenshotBytes);
        if (lastEncodedBytes > 0)
        {
            return Math.Max(8 * 1024, lastEncodedBytes);
        }

        if (width <= 0 || height <= 0)
        {
            return 32 * 1024;
        }

        return Math.Max(8 * 1024, (width * height) / 2);
    }

    private static void ReportEncodedScreenshotBytes(long byteCount)
    {
        if (byteCount <= 0 || byteCount > int.MaxValue)
        {
            return;
        }

        Volatile.Write(ref lastEncodedScreenshotBytes, (int)byteCount);
    }

    private static string ToBase64String(MemoryStream stream, int encodedLength)
    {
        if (stream.TryGetBuffer(out var encodedBuffer))
        {
            return Convert.ToBase64String(encodedBuffer.AsSpan(0, encodedLength));
        }

        return Convert.ToBase64String(stream.ToArray());
    }
}
