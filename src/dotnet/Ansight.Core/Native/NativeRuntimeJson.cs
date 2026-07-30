using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Pairing;

namespace Ansight.Native;

internal static class NativeRuntimeJson
{
    internal static string SerializeRequest(NativeHostConnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new JsonObject
        {
            ["kind"] = ToNativeRequestKind(request.Kind),
            ["payload"] = request.Payload,
            ["clientName"] = request.ClientName,
            ["expectedAppId"] = request.ExpectedAppId,
            ["hostAddressOverride"] = request.HostAddressOverride
        }.ToJsonString();
    }

    internal static HostConnectionResult ParseHostConnectionResult(string? json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        var success = GetBoolean(root, "success");
        var message = GetString(root, "message")
                      ?? (success ? "Host connection operation completed." : "Host connection operation failed.");

        return new HostConnectionResult(
            success,
            message,
            ParseActionKind(GetString(root, "kind")),
            ParseSource(GetString(root, "source")),
            GetString(root, "reasonCode"));
    }

    internal static OperationResult ParseOperationResult(string? json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        var success = GetBoolean(root, "success");
        var message = GetString(root, "message")
                      ?? (success ? "Operation completed." : "Operation failed.");
        return success
            ? OperationResult.FromSuccess(message)
            : OperationResult.FromFailure(message);
    }

    internal static HostConnectionStatus ParseHostConnectionStatus(string? json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        return new HostConnectionStatus(
            GetBoolean(root, "isRuntimeActive"),
            GetBoolean(root, "isConnected"),
            ParseConnectionState(GetString(root, "connectionState")),
            GetBoolean(root, "hasCachedSession"),
            GetBoolean(root, "hasSavedConfig"),
            GetBoolean(root, "hasBundledConfig"),
            ParseSummaryKind(GetString(root, "summaryKind")),
            GetString(root, "summaryMessage") ?? "Ansight host connection status is unavailable.");
    }

    internal static HostConnectionCapabilities ParseHostConnectionCapabilities(string? json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        return new HostConnectionCapabilities(
            GetBoolean(root, "canConnectUsingSavedConfig"),
            GetBoolean(root, "canConnectUsingBundledConfig"),
            GetBoolean(root, "canChooseConfigFile"),
            GetBoolean(root, "canScanConfigQrCode"),
            GetBoolean(root, "canClearSavedConfigs"));
    }

    internal static NativeTelemetrySnapshot ParseTelemetrySnapshot(string? json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        var metrics = new List<NativeRecordedMetric>();
        var events = new List<NativeRecordedEvent>();

        if (root.TryGetProperty("metrics", out var metricsElement) &&
            metricsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in metricsElement.EnumerateArray())
            {
                if (!TryGetChannel(metric, out var channel))
                {
                    continue;
                }

                metrics.Add(new NativeRecordedMetric(
                    GetInt64(metric, "value"),
                    channel,
                    GetCapturedAtUtc(metric),
                    GetInt64(metric, "sequence")));
            }
        }

        if (root.TryGetProperty("events", out var eventsElement) &&
            eventsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var nativeEvent in eventsElement.EnumerateArray())
            {
                if (!TryGetChannel(nativeEvent, out var channel))
                {
                    continue;
                }

                events.Add(new NativeRecordedEvent(
                    GetString(nativeEvent, "label") ?? "Native event",
                    ParseEventType(GetString(nativeEvent, "type")),
                    GetString(nativeEvent, "details") ?? string.Empty,
                    channel,
                    GetCapturedAtUtc(nativeEvent),
                    GetString(nativeEvent, "externalId"),
                    GetInt64(nativeEvent, "sequence")));
            }
        }

        return new NativeTelemetrySnapshot(metrics, events);
    }

    private static JsonDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The native Ansight bridge returned an empty JSON response.");
        }

        return JsonDocument.Parse(json);
    }

    private static bool GetBoolean(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           value.GetBoolean();

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetInt64(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var result)
            ? result
            : 0;

    private static bool TryGetChannel(JsonElement root, out byte channel)
    {
        var value = GetInt64(root, "channel");
        if (value is < byte.MinValue or > byte.MaxValue)
        {
            channel = 0;
            return false;
        }

        channel = (byte)value;
        return true;
    }

    private static DateTime GetCapturedAtUtc(JsonElement root)
    {
        var epochMilliseconds = GetInt64(root, "capturedAtEpochMs");
        if (epochMilliseconds > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds).UtcDateTime;
        }

        var capturedAtUtc = GetString(root, "capturedAtUtc");
        return DateTimeOffset.TryParse(capturedAtUtc, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow;
    }

    private static AppEventType ParseEventType(string? value)
        => Normalize(value) switch
        {
            "event" => AppEventType.Event,
            "debug" => AppEventType.Debug,
            "warning" => AppEventType.Warning,
            "error" => AppEventType.Error,
            "exception" => AppEventType.Exception,
            "gc" => AppEventType.Gc,
            "navigation" => AppEventType.Navigation,
            "screenviewed" => AppEventType.ScreenViewed,
            "lifecycle" => AppEventType.Lifecycle,
            _ => AppEventType.Info
        };

    private static string ToNativeRequestKind(HostConnectionRequestKind kind) => kind switch
    {
        HostConnectionRequestKind.SavedConfig => "savedConfig",
        HostConnectionRequestKind.BundledConfig => "bundledConfig",
        HostConnectionRequestKind.File => "file",
        HostConnectionRequestKind.QrCode => "qrCode",
        HostConnectionRequestKind.Payload => "payload",
        HostConnectionRequestKind.Config => "config",
        _ => "auto"
    };

    private static HostConnectionActionKind ParseActionKind(string? value) => Normalize(value) switch
    {
        "autoconnect" or "auto" => HostConnectionActionKind.AutoConnect,
        "savedconfig" or "connectusingsavedconfig" => HostConnectionActionKind.ConnectUsingSavedConfig,
        "bundledconfig" or "connectusingbundledconfig" => HostConnectionActionKind.ConnectUsingBundledConfig,
        "payload" or "file" or "qrcode" or "config" or "connectfrompayload" => HostConnectionActionKind.ConnectFromPayload,
        "disconnect" => HostConnectionActionKind.Disconnect,
        "clearsavedconfig" or "clearsavedconfigs" => HostConnectionActionKind.ClearSavedConfigs,
        "connectusingcachedsession" => HostConnectionActionKind.ConnectUsingCachedSession,
        "notifyconfigchanged" => HostConnectionActionKind.NotifyConfigChanged,
        "connect" => HostConnectionActionKind.Connect,
        _ => HostConnectionActionKind.None
    };

    private static HostConnectionSource ParseSource(string? value) => Normalize(value) switch
    {
        "autoprobe" => HostConnectionSource.AutoProbe,
        "cachedsession" => HostConnectionSource.CachedSession,
        "savedconfig" => HostConnectionSource.SavedConfig,
        "bundledconfig" => HostConnectionSource.BundledConfig,
        "payload" => HostConnectionSource.Payload,
        "configreader" => HostConnectionSource.ConfigReader,
        "hostconnection" => HostConnectionSource.HostConnection,
        "transport" => HostConnectionSource.Transport,
        "telemetry" => HostConnectionSource.Telemetry,
        "appstate" => HostConnectionSource.AppState,
        "sessionjpegcapture" => HostConnectionSource.SessionJpegCapture,
        "touchcapture" => HostConnectionSource.TouchCapture,
        _ => HostConnectionSource.None
    };

    private static HostConnectionState ParseConnectionState(string? value) => Normalize(value) switch
    {
        "connecting" => HostConnectionState.Connecting,
        "connected" => HostConnectionState.Connected,
        _ => HostConnectionState.Disconnected
    };

    private static HostConnectionSummaryKind ParseSummaryKind(string? value) => Normalize(value) switch
    {
        "runtimeinactive" => HostConnectionSummaryKind.RuntimeInactive,
        "disconnectednoconfigs" or "none" => HostConnectionSummaryKind.DisconnectedNoConfigs,
        "disconnectedcachedsessionavailable" => HostConnectionSummaryKind.DisconnectedCachedSessionAvailable,
        "disconnectedsavedconfigavailable" => HostConnectionSummaryKind.DisconnectedSavedConfigAvailable,
        "disconnectedbundledconfigavailable" => HostConnectionSummaryKind.DisconnectedBundledConfigAvailable,
        "disconnectedmultipleconfigsavailable" or "ready" => HostConnectionSummaryKind.DisconnectedMultipleConfigsAvailable,
        "connecting" => HostConnectionSummaryKind.Connecting,
        "connected" => HostConnectionSummaryKind.Connected,
        "disconnected" => HostConnectionSummaryKind.DisconnectedNoConfigs,
        _ => HostConnectionSummaryKind.RuntimeUnavailable
    };

    private static string Normalize(string? value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
