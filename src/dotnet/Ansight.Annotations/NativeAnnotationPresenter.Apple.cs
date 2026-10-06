#if IOS || MACCATALYST
namespace Ansight.Annotations;

using Ansight.Native.Apple;

internal static class NativeAnnotationPresenter
{
    internal static async Task<AnnotationCaptureResult> PresentAsync(object? host, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ANSDotNetRuntime.PresentAnnotation(json => { completion.TrySetResult(json); });
        var json = await completion.Task.WaitAsync(cancellationToken);
        return NativeAnnotationResult.Parse(json);
    }
}
#endif
