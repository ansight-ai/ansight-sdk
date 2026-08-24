using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Tools;

namespace Ansight.Native;

internal static class NativeRuntimeOptionsJson
{
    internal static string Serialize(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var root = new JsonObject
        {
            ["sampleFrequencyMilliseconds"] = options.SampleFrequencyMilliseconds,
            ["retentionPeriodSeconds"] = options.RetentionPeriodSeconds,
            ["enableFramesPerSecond"] = options.EnableFramesPerSecond,
            ["enableBatteryLevel"] = options.EnableBatteryLevel,
            ["enableOpenFileHandleTracking"] = options.EnableOpenFileHandleTracking,
            ["enableJniReferenceCountTracking"] = options.EnableJniReferenceCountTracking,
            ["defaultMemoryChannels"] = (byte)options.DefaultMemoryChannels,
            ["additionalChannels"] = SerializeChannels(options),
            ["sessionJpegCapture"] = SerializeSessionJpegCapture(options.SessionJpegCapture),
            ["touchCapture"] = SerializeTouchCapture(options.TouchCapture),
            ["crashCapture"] = SerializeCrashCapture(options.CrashCapture),
            ["toolGuard"] = SerializeToolGuard(options.ToolGuard),
            ["customProperties"] = SerializeCustomProperties(options.CustomProperties),
            ["hostAutoProbe"] = SerializeHostAutoProbe(options.HostAutoProbe),
            ["hostConnection"] = SerializeHostConnection(options.HostConnection)
        };

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    private static JsonArray SerializeChannels(Options options)
    {
        var channels = new JsonArray();
        foreach (var channel in options.AdditionalChannels ?? [])
        {
            channels.Add(new JsonObject
            {
                ["id"] = channel.Id,
                ["name"] = channel.Name,
                ["color"] = channel.Color.IsEmpty
                    ? null
                    : $"#{channel.Color.R:X2}{channel.Color.G:X2}{channel.Color.B:X2}",
                ["type"] = "custom"
            });
        }

        return channels;
    }

    private static JsonNode? SerializeSessionJpegCapture(SessionJpegCaptureOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["intervalMilliseconds"] = options.IntervalMilliseconds,
            ["quality"] = options.Quality,
            ["maxWidth"] = options.MaxWidth,
            ["captureGpuBackedSurfaces"] = options.CaptureGpuBackedSurfaces,
            ["captureKeyboardPresence"] = options.CaptureKeyboardPresence,
            ["mode"] = options.Mode switch
            {
                SessionJpegCaptureMode.ScreenshotAndVisualTree => "screenshotAndVisualTree",
                SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch => "screenshotWithVisualTreeOnTouch",
                _ => "screenshotOnly"
            }
        };
    }

    private static JsonNode? SerializeTouchCapture(TouchCaptureOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["captureMoveEvents"] = options.CaptureMoveEvents,
            ["captureCancelEvents"] = options.CaptureCancelEvents,
            ["moveCaptureDistanceThreshold"] = options.MoveCaptureDistanceThreshold,
            ["moveCaptureFramesPerSecond"] = options.MoveCaptureFramesPerSecond
        };
    }

    private static JsonObject SerializeCrashCapture(CrashCaptureOptions? options)
    {
        options ??= new CrashCaptureOptions();
        return new JsonObject
        {
            ["enabled"] = options.Enabled,
            ["studioHandoffEnabled"] = options.StudioHandoffEnabled,
            ["offlineCaptureAttachmentEnabled"] = options.OfflineCaptureAttachmentEnabled,
            ["maximumPendingReports"] = options.MaximumPendingReports,
            ["retentionDays"] = options.RetentionDays,
            ["maximumBreadcrumbs"] = options.MaximumBreadcrumbs,
            ["maximumTraceBytes"] = options.MaximumTraceBytes
        };
    }

    private static string SerializeToolGuard(ToolGuard? toolGuard)
    {
        if (toolGuard is null || !toolGuard.DiscoveryEnabled || !toolGuard.ExecutionEnabled)
        {
            return "disabled";
        }

        if (toolGuard.MaxPolicy == ToolPolicy.Critical)
        {
            return "fullAccess";
        }
        if (toolGuard.MaxPolicy == ToolPolicy.Write)
        {
            return "readWrite";
        }
        return "readOnly";
    }

    private static JsonObject SerializeCustomProperties(SessionCustomProperties? customProperties)
    {
        var result = new JsonObject();
        var effectiveProperties = DotNetSessionProperties.CreateEffective(customProperties);
        var source = effectiveProperties.ToJsonObject();

        foreach (var group in source)
        {
            if (group.Value is not JsonObject sourceProperties)
            {
                continue;
            }

            var properties = new JsonObject();
            foreach (var property in sourceProperties)
            {
                properties[property.Key] = ScalarToString(property.Value);
            }
            result[group.Key] = properties;
        }

        return result;
    }

    private static string ScalarToString(JsonNode? value)
    {
        if (value is null)
        {
            return string.Empty;
        }
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            return text;
        }
        if (value is JsonValue booleanValue && booleanValue.TryGetValue<bool>(out var boolean))
        {
            return boolean ? "true" : "false";
        }
        if (value is JsonValue numericValue &&
            numericValue.TryGetValue<double>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
        return value.ToJsonString();
    }

    private static JsonObject SerializeHostAutoProbe(HostAutoProbeOptions? options)
    {
        options ??= HostAutoProbeOptions.DisabledDefault;
        return new JsonObject
        {
            ["enabled"] = options.Enabled,
            ["initialDelayMilliseconds"] = (long)options.InitialDelay.TotalMilliseconds,
            ["probeIntervalMilliseconds"] = (long)options.ProbeInterval.TotalMilliseconds,
            ["reconnectDelayMilliseconds"] = (long)options.ReconnectDelay.TotalMilliseconds,
            ["clientName"] = options.ClientName
        };
    }

    private static JsonObject SerializeHostConnection(HostConnectionOptions? options)
    {
        options ??= HostConnectionOptions.Default;
        return new JsonObject
        {
            ["savedConfigKey"] = ResolveSavedConfigKey(options),
            ["bundledConfigJson"] = ResolveBundledConfig(options),
            ["connectionProfileRetentionSeconds"] = (long)options.ConnectionProfileRetention.TotalSeconds,
            ["discoveryPort"] = options.DiscoveryPort,
            ["allowCellularConnections"] = options.AllowCellularConnections,
            ["allowUnattendedProvisioning"] = options.AllowUnattendedProvisioning
        };
    }

    private static string ResolveSavedConfigKey(HostConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SavedConfigPath))
        {
            return "ai.ansight.dotnet.saved-pairing";
        }

        return $"ai.ansight.dotnet.saved-pairing.{Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(options.SavedConfigPath))).ToLowerInvariant()}";
    }

    private static string? ResolveBundledConfig(HostConnectionOptions options)
    {
        try
        {
            if (options.BundledConfigLoader is not null)
            {
                return Normalize(
                    options.BundledConfigLoader(CancellationToken.None)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult());
            }

            var assembly = options.BundledConfigAssembly;
            if (assembly is null)
            {
                return null;
            }

            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(
                        $".{HostConnectionOptions.BundledConfigAssetName}",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        name,
                        HostConnectionOptions.BundledConfigAssetName,
                        StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return Normalize(reader.ReadToEnd());
        }
        catch (Exception exception)
        {
            Logger.Warning($"The bundled Ansight registration could not be loaded for the native runtime: {exception.Message}");
            return null;
        }
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
