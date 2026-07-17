namespace Ansight.Tools.VisualTree;

using Ansight.Screenshot;
using Ansight.Pairing;
using System.Net.WebSockets;
using System.Text;
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

internal static partial class VisualTreeSupport
{
    private static int lastEncodedScreenshotBytes = 32 * 1024;

    internal static Task<ToolResult> GetNativeVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
    {
        var includeBounds = GetBoolean(arguments, "includeBounds", defaultValue: true);
        var includeProperties = GetBoolean(arguments, "includeComputedStyles", defaultValue: false);
        var maxDepth = GetInt(arguments, "maxDepth", defaultValue: 8, minimum: 1, maximum: 64);
        var maxNodes = GetInt(arguments, "maxNodes", defaultValue: 2000, minimum: 1, maximum: 100_000);
        var rootNodeId = GetString(arguments, "rootNodeId");

        return RunOnUiThreadAsync(() =>
        {
            var captureDepth = string.IsNullOrWhiteSpace(rootNodeId) ? maxDepth : 64;
            if (!TryCaptureTree(
                includeProperties,
                captureDepth,
                maxNodes,
                out var rootNode,
                out var nodeCount,
                out var truncated,
                out var error))
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
                ["format"] = "ansight.native.visual-tree.v1",
                ["platform"] = CurrentPlatform,
                ["source"] = VisualTreeProviderRegistry.NativeSource,
                ["adapter"] = CurrentPlatform == "android" ? "android.views" : "apple.uikit",
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["root"] = selectedRoot.ToJson(includeBounds, includeProperties, maxDepth),
                ["nodeCount"] = nodeCount,
                ["truncated"] = truncated
            };

            return ToolResult.Success(payload);
        });
    }

    internal static Task<ToolResult> InspectNativeNodeAsync(IReadOnlyDictionary<string, string> arguments)
    {
        var nodeId = GetRequiredString(arguments, "nodeId");
        var includeAncestors = GetBoolean(arguments, "includeAncestors", defaultValue: false);
        var includeDescendants = GetBoolean(arguments, "includeDescendants", defaultValue: false);
        var includeProperties = GetBoolean(arguments, "includeProperties", defaultValue: true);

        return RunOnUiThreadAsync(() =>
        {
            if (!TryCaptureTree(
                includeProperties,
                maxDepth: 64,
                maxNodes: 100_000,
                out var rootNode,
                out _,
                out _,
                out var error))
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
                ["source"] = VisualTreeProviderRegistry.NativeSource,
                ["adapter"] = CurrentPlatform == "android" ? "android.views" : "apple.uikit",
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
        var afterScreenUpdates = GetBoolean(arguments, "afterScreenUpdates", defaultValue: true);

#if ANDROID
        return RunOnUiThreadAsync(async () =>
        {
            var screenshotResult = await CaptureScreenshotAsync(format, quality, maxWidth, annotateNodeIds);
            if (screenshotResult.Screenshot == null)
            {
                return ToolResult.Failure(screenshotResult.Error ?? "Unable to capture a screenshot.", errorCode: "visual_screenshot_failed");
            }

            return CreateScreenshotResult(arguments, screenshotResult.Screenshot);
        });
#else
        return RunOnUiThreadAsync(() =>
        {
            if (!TryCaptureScreenshot(format, quality, maxWidth, annotateNodeIds, afterScreenUpdates, out var screenshot, out var error))
            {
                return ToolResult.Failure(error ?? "Unable to capture a screenshot.", errorCode: "visual_screenshot_failed");
            }

            return CreateScreenshotResult(arguments, screenshot!);
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
            int childCount,
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
            ChildCount = childCount;
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
        internal int ChildCount { get; }
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
                ["childCount"] = ChildCount
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

    private sealed class VisualTreeCaptureState(int maxNodes)
    {
        internal int MaxNodes { get; } = maxNodes;

        internal int NodeCount { get; private set; }

        internal bool Truncated { get; private set; }

        internal bool TryAddNode()
        {
            if (NodeCount >= MaxNodes)
            {
                Truncated = true;
                return false;
            }

            NodeCount++;
            return true;
        }

        internal void MarkTruncated()
        {
            Truncated = true;
        }
    }

    private static ToolResult CreateScreenshotResult(IReadOnlyDictionary<string, string> arguments, ScreenshotCapture screenshot)
    {
        var capturedAtUtc = DateTime.UtcNow;
        if (TryCreateBinaryScreenshotResult(arguments, screenshot, capturedAtUtc, out var binaryPayload, out var binaryError))
        {
            return ToolResult.Success(binaryPayload);
        }

        return ToolResult.Failure(
            string.IsNullOrWhiteSpace(binaryError)
                ? "Screenshot capture requires a live binary transfer channel."
                : binaryError,
            errorCode: "visual_screenshot_binary_transfer_unavailable");
    }

    private static bool TryCreateBinaryScreenshotResult(
        IReadOnlyDictionary<string, string> arguments,
        ScreenshotCapture screenshot,
        DateTime capturedAtUtc,
        out JsonObject payload,
        out string error)
    {
        payload = new JsonObject();
        error = string.Empty;

        var requestId = GetString(arguments, ToolExecutionArgumentNames.RequestId);
        if (string.IsNullOrWhiteSpace(requestId))
        {
            error = "Screenshot capture requires a live tool request id for binary transfer.";
            return false;
        }

        if (!Runtime.IsInitialized)
        {
            error = "Binary screenshot transfer requires an initialized Ansight runtime.";
            return false;
        }

        var transferHub = Runtime.MutableInstance.BinaryTransferHub;
        var transferId = Guid.NewGuid();
        var bytes = screenshot.Bytes;
        var pendingTransfer = new PairingBinaryTransferHub.PendingBinaryTransfer(
            description: $"ui.get_screenshot:{transferId:N}",
            startAsync: (transport, cancellationToken) => StreamScreenshotBytesAsync(
                transport,
                transferId,
                bytes,
                cancellationToken));

        if (!transferHub.TryQueueTransfer(requestId, pendingTransfer, out error))
        {
            return false;
        }

        payload = CreateScreenshotMetadataPayload(screenshot, capturedAtUtc);
        payload["deliveryMode"] = "websocket_binary";
        payload["wireProtocol"] = PairingFileTransferWireProtocol.ProtocolName;
        payload["transferId"] = transferId.ToString("N");
        payload["downloadId"] = requestId;
        payload["sizeBytes"] = bytes.LongLength;
        payload["fileName"] = $"screenshot-{capturedAtUtc:yyyyMMdd-HHmmssfff}.{ResolveScreenshotExtension(screenshot.Format)}";
        payload["mimeType"] = ResolveScreenshotMimeType(screenshot.Format);
        payload["status"] = "queued";
        return true;
    }

    private static JsonObject CreateScreenshotMetadataPayload(ScreenshotCapture screenshot, DateTime capturedAtUtc)
    {
        return new JsonObject
        {
            ["platform"] = CurrentPlatform,
            ["capturedAtUtc"] = capturedAtUtc.ToString("O"),
            ["format"] = screenshot.Format,
            ["width"] = screenshot.Width,
            ["height"] = screenshot.Height,
            ["annotationApplied"] = screenshot.AnnotationApplied
        };
    }

    private static async Task StreamScreenshotBytesAsync(
        PairingSessionTransport transport,
        Guid transferId,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        const int chunkBytes = 64 * 1024;
        var sequence = 0;
        var offsetBytes = 0L;

        try
        {
            while (offsetBytes < bytes.LongLength)
            {
                var bytesToSend = (int)Math.Min(chunkBytes, bytes.LongLength - offsetBytes);
                var frame = PairingFileTransferWireProtocol.CreateFrame(
                    transferId,
                    PairingFileTransferFrameType.Chunk,
                    sequence,
                    offsetBytes,
                    bytes.AsSpan((int)offsetBytes, bytesToSend));
                await SendScreenshotTransferFrameAsync(transport, frame, cancellationToken);
                sequence++;
                offsetBytes += bytesToSend;
            }

            var completeFrame = PairingFileTransferWireProtocol.CreateFrame(
                transferId,
                PairingFileTransferFrameType.Complete,
                sequence,
                offsetBytes,
                ReadOnlySpan<byte>.Empty);
            await SendScreenshotTransferFrameAsync(transport, completeFrame, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TrySendScreenshotTransferErrorFrameAsync(transport, transferId, sequence, offsetBytes, exception, cancellationToken);
        }
    }

    private static async Task SendScreenshotTransferFrameAsync(
        PairingSessionTransport transport,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        var result = await transport.SendBinaryAsync(frame, WebSocketMessageType.Binary, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    private static async Task TrySendScreenshotTransferErrorFrameAsync(
        PairingSessionTransport transport,
        Guid transferId,
        int sequence,
        long offsetBytes,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(exception.Message);
            var frame = PairingFileTransferWireProtocol.CreateFrame(
                transferId,
                PairingFileTransferFrameType.Error,
                sequence,
                offsetBytes,
                payload);
            _ = await transport.SendBinaryAsync(frame, WebSocketMessageType.Binary, cancellationToken);
        }
        catch
        {
        }
    }

    private static string ResolveScreenshotExtension(string format)
        => string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";

    private static string ResolveScreenshotMimeType(string format)
        => string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg"
            : "image/png";

    private sealed record ScreenshotCapture(string Format, int Width, int Height, byte[] Bytes, bool AnnotationApplied);

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

    private static bool TryCaptureTree(
        bool includeProperties,
        int maxDepth,
        int maxNodes,
        out VisualNode? rootNode,
        out int nodeCount,
        out bool truncated,
        out string? error)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null)
        {
            rootNode = null;
            nodeCount = 0;
            truncated = false;
            error = "No Android root view is currently available.";
            return false;
        }

        var state = new VisualTreeCaptureState(maxNodes);
        rootNode = BuildAndroidNode(rootView, rootView, includeProperties, maxDepth, state);
        nodeCount = state.NodeCount;
        truncated = state.Truncated;
        error = null;
        return true;
    }

    private static VisualNode BuildAndroidNode(
        View view,
        View rootView,
        bool includeProperties,
        int remainingDepth,
        VisualTreeCaptureState state)
    {
        state.TryAddNode();
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
        var childCount = 0;
        if (view is ViewGroup group)
        {
            childCount = group.ChildCount;
            if (remainingDepth <= 0 && childCount > 0)
            {
                state.MarkTruncated();
            }

            for (var index = 0; remainingDepth > 0 && index < childCount; index++)
            {
                if (state.NodeCount >= state.MaxNodes)
                {
                    state.MarkTruncated();
                    break;
                }

                var child = group.GetChildAt(index);
                if (child != null)
                {
                    children.Add(BuildAndroidNode(child, rootView, includeProperties, remainingDepth - 1, state));
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
            childCount: childCount,
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
        using var windowBitmap = Bitmap.CreateBitmap(rootView.Width, rootView.Height, Bitmap.Config.Argb8888!);
        using (var canvas = new Canvas(bitmap))
        {
            var captureResult = await AndroidSceneCapture.CaptureAsync(
                new AndroidSceneCaptureRoot(activity!, rootView),
                bitmap,
                canvas,
                windowBitmap,
                CancellationToken.None);
            if (!captureResult.Success)
            {
                return new AndroidScreenshotCaptureResult(null, captureResult.Error ?? "Android scene capture failed.");
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
                Bytes: stream.ToArray(),
                AnnotationApplied: false && annotateNodeIds),
                null);
        }
        finally
        {
            scaledBitmap?.Dispose();
        }
    }

    private sealed record AndroidScreenshotCaptureResult(ScreenshotCapture? Screenshot, string? Error);

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

        public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
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

        public void OnActivityPaused(Activity activity) { }
        public void OnActivityResumed(Activity activity)
        {
            lock (sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
        public void OnActivityStarted(Activity activity)
        {
            lock (sync)
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

    private static bool TryCaptureTree(
        bool includeProperties,
        int maxDepth,
        int maxNodes,
        out VisualNode? rootNode,
        out int nodeCount,
        out bool truncated,
        out string? error)
    {
        var window = GetActiveWindow();
        if (window == null)
        {
            rootNode = null;
            nodeCount = 0;
            truncated = false;
            error = "No active UIWindow is available.";
            return false;
        }

        var state = new VisualTreeCaptureState(maxNodes);
        rootNode = BuildAppleNode(window, window, includeProperties, maxDepth, state);
        nodeCount = state.NodeCount;
        truncated = state.Truncated;
        error = null;
        return true;
    }

    private static VisualNode BuildAppleNode(
        UIView view,
        UIWindow window,
        bool includeProperties,
        int remainingDepth,
        VisualTreeCaptureState state)
    {
        state.TryAddNode();
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

        var subviews = view.Subviews;
        var children = new List<VisualNode>(subviews.Length);
        if (remainingDepth <= 0 && subviews.Length > 0)
        {
            state.MarkTruncated();
        }

        foreach (var child in subviews)
        {
            if (remainingDepth <= 0 || state.NodeCount >= state.MaxNodes)
            {
                if (state.NodeCount >= state.MaxNodes)
                {
                    state.MarkTruncated();
                }

                break;
            }

            children.Add(BuildAppleNode(child, window, includeProperties, remainingDepth - 1, state));
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
            childCount: subviews.Length,
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

    private static bool TryCaptureScreenshot(
        string format,
        int quality,
        int? maxWidth,
        bool annotateNodeIds,
        bool afterScreenUpdates,
        out ScreenshotCapture? screenshot,
        out string? error)
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
                window.DrawViewHierarchy(originalBounds, afterScreenUpdates);
            })
            : renderer.CreatePng(renderContext =>
            {
                renderContext.CGContext.ScaleCTM((nfloat)(targetSize.Width / originalBounds.Width), (nfloat)(targetSize.Height / originalBounds.Height));
                window.DrawViewHierarchy(originalBounds, afterScreenUpdates);
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
            Bytes: imageData.ToArray(),
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

        return null;
    }

    private static nfloat GetRenderScale(UIWindow window)
    {
        var scale = window.Screen?.Scale ?? UIScreen.MainScreen.Scale;
        return scale > 0 ? scale : 1;
    }
#else
    private static Task<ToolResult> RunOnUiThreadAsync(Func<ToolResult> action) => Task.FromResult(ToolResult.Failure("Visual tree tools are only supported on Android, iOS, and Mac Catalyst.", errorCode: "visual_tree_platform_unsupported"));
    private static bool TryCaptureTree(
        bool includeProperties,
        int maxDepth,
        int maxNodes,
        out VisualNode? rootNode,
        out int nodeCount,
        out bool truncated,
        out string? error)
    {
        rootNode = null;
        nodeCount = 0;
        truncated = false;
        error = "Visual tree capture is not available on this platform.";
        return false;
    }

    private static bool TryCaptureScreenshot(
        string format,
        int quality,
        int? maxWidth,
        bool annotateNodeIds,
        bool afterScreenUpdates,
        out ScreenshotCapture? screenshot,
        out string? error)
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

}
