#if ANDROID
namespace Ansight.Annotations;

using AI.Ansight.Dotnet;
using Android.App;

internal static class NativeAnnotationPresenter
{
    internal static async Task<AnnotationCaptureResult> PresentAsync(object? host, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AnnotationResultHandler(completion);
        AnsightDotNetBridge.PresentAnnotation(host as Activity, handler);
        var json = await completion.Task.WaitAsync(cancellationToken);
        GC.KeepAlive(handler);
        return NativeAnnotationResult.Parse(json);
    }

    private sealed class AnnotationResultHandler(TaskCompletionSource<string?> completion)
        : Java.Lang.Object, AnsightDotNetBridge.IAnnotationResultHandler
    {
        public void Complete(string? resultJson) => completion.TrySetResult(resultJson);
    }
}
#endif
