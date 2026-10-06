namespace Ansight.Annotations;

using System.Text.Json;

internal static class NativeAnnotationResult
{
    internal static AnnotationCaptureResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AnnotationCaptureResult(AnnotationCaptureStatus.Failed, message: "The native annotation bridge returned no result.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var statusName = root.GetProperty("status").GetString();
            if (!Enum.TryParse<AnnotationCaptureStatus>(statusName, true, out var status))
            {
                status = AnnotationCaptureStatus.Failed;
            }

            Guid? annotationId = null;
            if (root.TryGetProperty("annotationId", out var idValue) &&
                idValue.ValueKind == JsonValueKind.String &&
                Guid.TryParse(idValue.GetString(), out var id))
            {
                annotationId = id;
            }

            var message = root.TryGetProperty("message", out var messageValue) && messageValue.ValueKind == JsonValueKind.String
                ? messageValue.GetString()
                : null;
            return new AnnotationCaptureResult(status, annotationId, message);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            return new AnnotationCaptureResult(AnnotationCaptureStatus.Failed, message: exception.Message);
        }
    }
}
