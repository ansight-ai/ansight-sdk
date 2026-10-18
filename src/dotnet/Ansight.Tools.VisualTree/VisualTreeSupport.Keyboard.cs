using System.Text.Json.Nodes;

namespace Ansight.Tools.VisualTree;

internal static partial class VisualTreeSupport
{
    internal static Task<ToolResult> SubmitKeyboardAsync()
    {
#if ANDROID
        return RunOnUiThreadAsync(() =>
        {
            var activity = AndroidActivityTracker.GetCurrentActivity();
            var view = activity?.CurrentFocus;
            if (activity?.HasWindowFocus != true || view is null)
                return ToolResult.Failure("No focused editor is available.", errorCode: "keyboard_no_focused_editor");
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = view.OnCreateInputConnection(info);
            var action = info.ActionLabel is not null ? (int)info.ActionId : (int)info.ImeOptions & 0xff;
            if (connection is null || info.ActionLabel is null && action is 0 or 1)
                return ToolResult.Failure("The focused editor has no submit action. Use keyboard.press for a literal Enter.", errorCode: "keyboard_submit_unavailable");
            var performed = connection.PerformEditorAction((Android.Views.InputMethods.ImeAction)action);
            return performed ? ToolResult.Success(new JsonObject { ["performed"] = true })
                : ToolResult.Failure("The focused editor rejected the action.", errorCode: "keyboard_submit_unavailable");
        });
#elif IOS || MACCATALYST
        return RunOnUiThreadAsync(() =>
        {
            static UIKit.UIView? FindFocused(UIKit.UIView view)
                => view.IsFirstResponder ? view : view.Subviews.Select(FindFocused).FirstOrDefault(child => child is not null);
            var window = GetActiveWindow();
            if (window is null || FindFocused(window) is not UIKit.UITextField field)
                return ToolResult.Failure("The focused view does not expose a submit action. Use keyboard.press for a literal Enter.", errorCode: "keyboard_submit_unavailable");
            var allowDefault = field.Delegate?.ShouldReturn(field) ?? true;
            if (allowDefault) field.SendActionForControlEvents(UIKit.UIControlEvent.EditingDidEndOnExit);
            return ToolResult.Success(new JsonObject { ["performed"] = true });
        });
#else
        return Task.FromResult(ToolResult.Failure("Keyboard submit is not supported on this platform.", errorCode: "keyboard_platform_unsupported"));
#endif
    }

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
