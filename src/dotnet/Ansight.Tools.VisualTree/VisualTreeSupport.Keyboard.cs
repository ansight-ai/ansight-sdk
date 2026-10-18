using System.Text.Json.Nodes;

namespace Ansight.Tools.VisualTree;

internal static partial class VisualTreeSupport
{
    internal static Task<ToolResult> DismissKeyboardAsync()
    {
#if ANDROID
        return RunOnUiThreadAsync(() =>
        {
            var activity = AndroidActivityTracker.GetCurrentActivity();
            var view = activity?.CurrentFocus ?? activity?.Window?.DecorView;
            if (activity is null || !activity.HasWindowFocus || view?.WindowToken is null)
                return ToolResult.Failure("No focused Android app window is available.", errorCode: "keyboard_no_focused_window");

            if (activity.GetSystemService(Android.Content.Context.InputMethodService)
                is not Android.Views.InputMethods.InputMethodManager inputMethod)
                return ToolResult.Failure("The Android input method service is unavailable.", errorCode: "keyboard_unavailable");

            // Never send Back: visibility may change between the host's observation and this call.
            var requested = inputMethod.HideSoftInputFromWindow(
                view.WindowToken, Android.Views.InputMethods.HideSoftInputFlags.None);
            return ToolResult.Success(new JsonObject { ["requested"] = requested });
        });
#elif IOS || MACCATALYST
        return RunOnUiThreadAsync(() =>
        {
            var window = GetActiveWindow();
            if (window is null)
                return ToolResult.Failure("No active app window is available.", errorCode: "keyboard_no_focused_window");

            return ToolResult.Success(new JsonObject { ["requested"] = window.EndEditing(true) });
        });
#else
        return Task.FromResult(ToolResult.Failure(
            "Keyboard dismissal is not supported on this platform.", errorCode: "keyboard_platform_unsupported"));
#endif
    }
}
