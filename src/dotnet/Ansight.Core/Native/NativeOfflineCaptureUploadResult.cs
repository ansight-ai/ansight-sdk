using System.Text.Json;

namespace Ansight.Native;

public sealed record NativeOfflineCaptureUploadResult(
    IReadOnlyList<string> SessionIds,
    int AcknowledgedChunks,
    int PendingChunks)
{
    internal static NativeOfflineCaptureUploadResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.GetString() ?? "Offline capture upload failed.");
        }

        var sessionIds = root.GetProperty("sessionIds").EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidDataException("Missing capture session ID."))
            .ToArray();

        return new NativeOfflineCaptureUploadResult(
            sessionIds,
            root.GetProperty("acknowledgedChunks").GetInt32(),
            root.GetProperty("pendingChunks").GetInt32());
    }
}
