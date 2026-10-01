using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ansight.Network;

namespace Ansight.OfflineCapture;

using TelemetryChannel = Ansight.Telemetry.Channels.Channel;

internal static class OfflineCaptureJson
{
    public static readonly JsonSerializerOptions Data = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Metadata = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions touchDetailsJson = new(Data)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Metric(Metric metric)
    {
        return JsonSerializer.Serialize(new
        {
            t = ToUnixMilliseconds(metric.CapturedAtUtc),
            c = metric.Channel,
            v = metric.Value
        }, Data);
    }

    public static string Event(AppEvent appEvent)
    {
        return JsonSerializer.Serialize(new
        {
            id = appEvent.Id.ToString("N"),
            t = ToUnixMilliseconds(appEvent.CapturedAtUtc),
            c = appEvent.Channel,
            k = (int)appEvent.Type,
            l = appEvent.Label,
            d = string.IsNullOrEmpty(appEvent.Details) ? null : appEvent.Details
        }, Data);
    }

    public static string Touch(CapturedTouch touch)
    {
        return JsonSerializer.Serialize(new
        {
            id = touch.Id.ToString("N"),
            t = touch.CapturedAtUtc.ToUnixTimeMilliseconds(),
            a = (int)touch.Action,
            p = touch.PointerId,
            i = touch.PointerIndex == 0 ? (int?)null : touch.PointerIndex,
            pc = touch.PointerCount == 1 ? (int?)null : touch.PointerCount,
            x = touch.X,
            y = touch.Y,
            w = touch.SurfaceWidth,
            h = touch.SurfaceHeight,
            u = touch.CoordinateUnit,
            s = touch.SurfaceScale,
            details = touch.Details is null
                ? (JsonElement?)null
                : JsonSerializer.SerializeToElement(touch.Details, touchDetailsJson)
        }, Data);
    }

    public static string Screenshot(SessionJpegFrame frame, string relativePath)
    {
        return JsonSerializer.Serialize(new
        {
            t = frame.CapturedAtUtc.ToUnixTimeMilliseconds(),
            p = relativePath.Replace('\\', '/'),
            w = frame.Width,
            h = frame.Height,
            q = frame.Quality,
            b = frame.JpegByteCount
        }, Data);
    }

    public static string NetworkRequest(NetworkRequestRecord request)
    {
        return JsonSerializer.Serialize(request, Data);
    }

    public static object Channel(TelemetryChannel channel)
    {
        return new
        {
            id = channel.Id,
            n = channel.Name,
            c = ToColorHex(channel.Color)
        };
    }

    private static long ToUnixMilliseconds(DateTime utc)
    {
        var offset = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return offset.ToUnixTimeMilliseconds();
    }

    private static string? ToColorHex(Color color)
    {
        if (color.IsEmpty)
        {
            return null;
        }

        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
