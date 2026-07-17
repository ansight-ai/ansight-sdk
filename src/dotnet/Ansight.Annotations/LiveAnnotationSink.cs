namespace Ansight.Annotations;

using System.Text.Json.Nodes;

internal sealed class LiveAnnotationSink : IAnnotationSink
{
    private const string SubmitAction = "annotation.submit";
    private readonly IRuntime runtime;

    internal LiveAnnotationSink(IRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Id => "studio.live";

    public async ValueTask<AnnotationSinkResult> SubmitAsync(
        AnnotationBundle bundle,
        CancellationToken cancellationToken)
    {
        if (!runtime.HostConnection.IsConnected)
        {
            return AnnotationSinkResult.Failure(Id, "A live Ansight Studio session is not connected.");
        }

        if (runtime is not RuntimeImpl runtimeImpl)
        {
            return AnnotationSinkResult.Failure(Id, "The runtime does not support annotation bundle transfer.");
        }

        var payload = new JsonObject
        {
            ["schema"] = "ansight.annotation.submit.v1",
            ["clientAnnotationId"] = bundle.AnnotationId.ToString("N"),
            ["capturedAtUtc"] = bundle.CapturedAtUtc.ToString("O")
        };
        var result = await runtimeImpl.SendBinaryExtensionAsync(
            SubmitAction,
            payload,
            bundle.FileName,
            bundle.MimeType,
            bundle.Bytes,
            cancellationToken);
        return result.Success
            ? AnnotationSinkResult.Success(Id, result.Message)
            : AnnotationSinkResult.Failure(Id, result.Message);
    }
}
