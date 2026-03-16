namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

#if ANDROID
using Android.App;
using Android.Graphics;
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

    private static bool TryCaptureScreenshot(string format, int quality, int? maxWidth, bool annotateNodeIds, out ScreenshotCapture? screenshot, out string? error)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            screenshot = null;
            error = "No Android root view is available for screenshot capture.";
            return false;
        }

        using var bitmap = Bitmap.CreateBitmap(rootView.Width, rootView.Height, Bitmap.Config.Argb8888!);
        using (var canvas = new Canvas(bitmap))
        {
            rootView.Draw(canvas);
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
            using var stream = new MemoryStream();
            var success = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
                ? workingBitmap.Compress(Bitmap.CompressFormat.Jpeg!, quality, stream)
                : workingBitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);

            if (!success)
            {
                screenshot = null;
                error = "Android bitmap compression failed.";
                return false;
            }

            screenshot = new ScreenshotCapture(
                Format: string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpeg" : "png",
                Width: workingBitmap.Width,
                Height: workingBitmap.Height,
                Base64: Convert.ToBase64String(stream.ToArray()),
                AnnotationApplied: false && annotateNodeIds);
            error = null;
            return true;
        }
        finally
        {
            scaledBitmap?.Dispose();
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
                return instance?.currentActivity;
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

        var targetSize = originalBounds.Size;
        if (maxWidth.HasValue && targetSize.Width > maxWidth.Value)
        {
            var scaleFactor = maxWidth.Value / (double)targetSize.Width;
            targetSize = new CGSize(maxWidth.Value, targetSize.Height * scaleFactor);
        }

        UIGraphics.BeginImageContextWithOptions(targetSize, false, 0);
        try
        {
            var context = UIGraphics.GetCurrentContext();
            if (context == null)
            {
                screenshot = null;
                error = "Unable to create a CoreGraphics screenshot context.";
                return false;
            }

            context.ScaleCTM((nfloat)(targetSize.Width / originalBounds.Width), (nfloat)(targetSize.Height / originalBounds.Height));
            window.DrawViewHierarchy(originalBounds, afterScreenUpdates: true);

            using var image = UIGraphics.GetImageFromCurrentImageContext();
            if (image == null)
            {
                screenshot = null;
                error = "Failed to render the current UIWindow.";
                return false;
            }

            using var imageData = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
                ? image.AsJPEG((nfloat)(quality / 100d))
                : image.AsPNG();

            if (imageData == null)
            {
                screenshot = null;
                error = "Failed to encode the rendered screenshot.";
                return false;
            }

            screenshot = new ScreenshotCapture(
                Format: string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpeg" : "png",
                Width: (int)Math.Round(targetSize.Width),
                Height: (int)Math.Round(targetSize.Height),
                Base64: Convert.ToBase64String(imageData.ToArray()),
                AnnotationApplied: false && annotateNodeIds);
            error = null;
            return true;
        }
        finally
        {
            UIGraphics.EndImageContext();
        }
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
}
