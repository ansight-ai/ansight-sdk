namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.ApplicationModel;

internal static partial class MauiToolHelpers
{
    internal static Task<ToolResult> RunOnMainThreadAsync(Func<ToolResult> action)
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                return Task.FromResult(ExecuteSafely(action));
            }

            return MainThread.InvokeOnMainThreadAsync(() => ExecuteSafely(action));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "maui_execution_failed"));
        }
    }

    internal static ToolResult ExecuteSafely(Func<ToolResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            return ToolResult.Failure(exception.Message, errorCode: "maui_execution_failed");
        }
    }

    internal static string CurrentPlatform
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
}
#endif
