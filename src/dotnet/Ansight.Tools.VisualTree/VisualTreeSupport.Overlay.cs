namespace Ansight.Tools.VisualTree;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

#if ANDROID
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
#elif IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;
#endif

internal static partial class VisualTreeSupport
{
    private const int DefaultOverlayDurationMilliseconds = 5000;
    private const int MaximumOverlayDurationMilliseconds = 10 * 60 * 1000;
    private const int MaximumOverlayRectangles = 128;
    private const int MaximumOverlayMetadataEntries = 16;
    private const int MaximumOverlayMetadataKeyLength = 64;
    private const int MaximumOverlayMetadataStringLength = 256;
    private const string DefaultOverlayStrokeColor = "#FF3B30";
    private static readonly Dictionary<string, OverlayEntry> overlays = new(StringComparer.Ordinal);

    internal static Task<ToolResult> ShowOverlayAsync(IReadOnlyDictionary<string, string> arguments)
        => RunOnUiThreadAsync(() => ShowOverlayCore(arguments));

    internal static Task<ToolResult> GetOverlayAsync(IReadOnlyDictionary<string, string> arguments)
        => RunOnUiThreadAsync(() =>
        {
            RemoveExpiredOverlays(DateTime.UtcNow);

            var overlayId = GetRequiredString(arguments, "overlayId");
            if (!overlays.TryGetValue(overlayId, out var entry))
            {
                return ToolResult.Failure($"The overlay '{overlayId}' was not found.", errorCode: "visual_overlay_not_found");
            }

            return ToolResult.Success(CreateOverlayResultPayload(entry));
        });

    internal static Task<ToolResult> QueryOverlaysAsync(IReadOnlyDictionary<string, string> arguments)
        => RunOnUiThreadAsync(() =>
        {
            RemoveExpiredOverlays(DateTime.UtcNow);

            var metadataKey = GetString(arguments, "metadataKey");
            var metadataValue = GetString(arguments, "metadataValue");
            var overlayArray = new JsonArray();
            foreach (var entry in overlays.Values.OrderBy(entry => entry.CreatedAtUtc))
            {
                if (!OverlayMatchesQuery(entry, metadataKey, metadataValue))
                {
                    continue;
                }

                overlayArray.Add(CreateOverlayJson(entry));
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["count"] = overlayArray.Count,
                ["overlays"] = overlayArray
            };

            return ToolResult.Success(payload);
        });

    internal static Task<ToolResult> UpdateOverlayAsync(IReadOnlyDictionary<string, string> arguments)
        => RunOnUiThreadAsync(() =>
        {
            RemoveExpiredOverlays(DateTime.UtcNow);

            var overlayId = GetRequiredString(arguments, "overlayId");
            if (!overlays.TryGetValue(overlayId, out var existingEntry))
            {
                return ToolResult.Failure($"The overlay '{overlayId}' was not found.", errorCode: "visual_overlay_not_found");
            }

            if (!TryCreateUpdatedOverlayEntry(existingEntry, arguments, out var updatedEntry, out var error) ||
                updatedEntry == null)
            {
                return ToolResult.Failure(error ?? "The overlay could not be updated.", errorCode: "visual_overlay_update_failed");
            }

            if (!TryAttachOverlayToPlatformWindow(updatedEntry, out var attachError))
            {
                return ToolResult.Failure(attachError ?? "The updated overlay could not be attached to the active window.", errorCode: "visual_overlay_attach_failed");
            }

            RemoveOverlayEntry(existingEntry);
            overlays[updatedEntry.Id] = updatedEntry;
            ScheduleOverlayExpiration(updatedEntry);
            return ToolResult.Success(CreateOverlayResultPayload(updatedEntry));
        });

    internal static Task<ToolResult> RemoveOverlayAsync(IReadOnlyDictionary<string, string> arguments)
        => RunOnUiThreadAsync(() =>
        {
            RemoveExpiredOverlays(DateTime.UtcNow);

            var overlayId = GetRequiredString(arguments, "overlayId");
            overlays.TryGetValue(overlayId, out var entry);
            if (entry != null)
            {
                RemoveOverlayEntry(entry);
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["overlayId"] = overlayId,
                ["removed"] = entry != null,
                ["overlay"] = entry == null ? null : CreateOverlayJson(entry)
            };

            return ToolResult.Success(payload);
        });

    internal static Task<ToolResult> ClearOverlaysAsync(IReadOnlyDictionary<string, string> arguments)
        => RunOnUiThreadAsync(() =>
        {
            RemoveExpiredOverlays(DateTime.UtcNow);

            var metadataKey = GetString(arguments, "metadataKey");
            var metadataValue = GetString(arguments, "metadataValue");
            var entries = overlays.Values
                .Where(entry => OverlayMatchesQuery(entry, metadataKey, metadataValue))
                .OrderBy(entry => entry.CreatedAtUtc)
                .ToArray();

            var overlayArray = new JsonArray();
            foreach (var entry in entries)
            {
                overlayArray.Add(CreateOverlayJson(entry));
                RemoveOverlayEntry(entry);
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["count"] = overlayArray.Count,
                ["overlays"] = overlayArray
            };

            return ToolResult.Success(payload);
        });

    private static ToolResult ShowOverlayCore(IReadOnlyDictionary<string, string> arguments)
    {
        RemoveExpiredOverlays(DateTime.UtcNow);

        if (!TryCreateOverlayEntry(arguments, out var entry, out var error) || entry == null)
        {
            return ToolResult.Failure(error ?? "The overlay could not be created.", errorCode: "visual_overlay_create_failed");
        }

        if (overlays.TryGetValue(entry.Id, out var existingEntry))
        {
            RemoveOverlayEntry(existingEntry);
        }

        if (!TryAttachOverlayToPlatformWindow(entry, out var attachError))
        {
            return ToolResult.Failure(attachError ?? "The overlay could not be attached to the active window.", errorCode: "visual_overlay_attach_failed");
        }

        overlays[entry.Id] = entry;
        ScheduleOverlayExpiration(entry);
        return ToolResult.Success(CreateOverlayResultPayload(entry));
    }

    private static bool TryCreateOverlayEntry(
        IReadOnlyDictionary<string, string> arguments,
        out OverlayEntry? entry,
        out string? error)
    {
        entry = null;

        var overlayId = GetString(arguments, "overlayId") ?? Guid.NewGuid().ToString("N");
        if (!IsValidOverlayId(overlayId))
        {
            error = "The overlayId argument must be non-empty and at most 128 characters.";
            return false;
        }

        if (!TryCreateOverlayStyle(arguments, out var style, out error))
        {
            return false;
        }

        if (!TryCreateOverlayMetadata(arguments, out var metadata, out error))
        {
            return false;
        }

        if (!TryCreateOverlayRectangles(arguments, out var rectangles, out error))
        {
            return false;
        }

        var durationMilliseconds = GetInt(
            arguments,
            "durationMs",
            defaultValue: DefaultOverlayDurationMilliseconds,
            minimum: 0,
            maximum: MaximumOverlayDurationMilliseconds);
        var createdAtUtc = DateTime.UtcNow;
        var expiresAtUtc = durationMilliseconds > 0
            ? createdAtUtc.AddMilliseconds(durationMilliseconds)
            : (DateTime?)null;

        entry = new OverlayEntry(
            overlayId,
            rectangles,
            style,
            metadata,
            createdAtUtc,
            expiresAtUtc,
            durationMilliseconds);
        error = null;
        return true;
    }

    private static bool TryCreateUpdatedOverlayEntry(
        OverlayEntry existingEntry,
        IReadOnlyDictionary<string, string> arguments,
        out OverlayEntry? entry,
        out string? error)
    {
        entry = null;

        if (!TryCreateUpdatedOverlayRectangles(existingEntry, arguments, out var rectangles, out error))
        {
            return false;
        }

        if (!TryCreateUpdatedOverlayStyle(existingEntry.Style, arguments, out var style, out error))
        {
            return false;
        }

        if (!TryCreateUpdatedOverlayMetadata(existingEntry.Metadata, arguments, out var metadata, out error))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var durationMilliseconds = existingEntry.DurationMilliseconds;
        var expiresAtUtc = existingEntry.ExpiresAtUtc;
        if (arguments.ContainsKey("durationMs"))
        {
            durationMilliseconds = GetInt(
                arguments,
                "durationMs",
                defaultValue: existingEntry.DurationMilliseconds,
                minimum: 0,
                maximum: MaximumOverlayDurationMilliseconds);
            expiresAtUtc = durationMilliseconds > 0
                ? now.AddMilliseconds(durationMilliseconds)
                : null;
        }

        entry = new OverlayEntry(
            existingEntry.Id,
            rectangles,
            style,
            metadata,
            existingEntry.CreatedAtUtc,
            expiresAtUtc,
            durationMilliseconds);
        error = null;
        return true;
    }

    private static bool IsValidOverlayId(string overlayId)
        => !string.IsNullOrWhiteSpace(overlayId) && overlayId.Length <= 128;

    private static bool TryCreateUpdatedOverlayRectangles(
        OverlayEntry existingEntry,
        IReadOnlyDictionary<string, string> arguments,
        out IReadOnlyList<OverlayRectangle> rectangles,
        out string? error)
    {
        if (!HasOverlayGeometryArguments(arguments))
        {
            rectangles = existingEntry.Rectangles;
            error = null;
            return true;
        }

        return TryCreateOverlayRectangles(arguments, out rectangles, out error);
    }

    private static bool TryCreateUpdatedOverlayStyle(
        OverlayStyle existingStyle,
        IReadOnlyDictionary<string, string> arguments,
        out OverlayStyle style,
        out string? error)
    {
        var strokeColor = existingStyle.StrokeColor;
        var fillColor = existingStyle.FillColor;
        var strokeWidth = existingStyle.StrokeWidth;
        var cornerRadius = existingStyle.CornerRadius;

        var rawStrokeColor = GetString(arguments, "strokeColor");
        if (rawStrokeColor != null)
        {
            if (!TryParseOverlayColor(rawStrokeColor, allowNone: false, out var parsedStrokeColor, out error) ||
                parsedStrokeColor == null)
            {
                style = default!;
                return false;
            }

            strokeColor = parsedStrokeColor.Value;
        }

        if (arguments.ContainsKey("fillColor"))
        {
            if (!TryParseOverlayColor(GetString(arguments, "fillColor"), allowNone: true, out var parsedFillColor, out error))
            {
                style = default!;
                return false;
            }

            fillColor = parsedFillColor;
        }

        if (arguments.ContainsKey("strokeWidth"))
        {
            strokeWidth = GetDouble(arguments, "strokeWidth", existingStyle.StrokeWidth, minimum: 0, maximum: 128);
        }

        if (arguments.ContainsKey("cornerRadius"))
        {
            cornerRadius = GetDouble(arguments, "cornerRadius", existingStyle.CornerRadius, minimum: 0, maximum: 256);
        }

        if (strokeWidth <= 0 && fillColor == null)
        {
            error = "The overlay would be invisible because strokeWidth is zero and fillColor is empty.";
            style = default!;
            return false;
        }

        style = new OverlayStyle(strokeColor, fillColor, strokeWidth, cornerRadius);
        error = null;
        return true;
    }

    private static bool TryCreateUpdatedOverlayMetadata(
        JsonObject existingMetadata,
        IReadOnlyDictionary<string, string> arguments,
        out JsonObject metadata,
        out string? error)
    {
        var metadataMode = NormalizeOverlayMetadataMode(GetString(arguments, "metadataMode"));
        switch (metadataMode)
        {
            case "clear":
                metadata = new JsonObject();
                error = null;
                return true;
            case "replace":
                return TryCreateOverlayMetadata(arguments, out metadata, out error);
            case "merge":
                metadata = (JsonObject)existingMetadata.DeepClone();
                if (!arguments.ContainsKey("metadata"))
                {
                    error = null;
                    return true;
                }

                if (!TryCreateOverlayMetadata(arguments, out var patchMetadata, out error))
                {
                    return false;
                }

                foreach (var property in patchMetadata)
                {
                    metadata[property.Key] = property.Value?.DeepClone();
                }

                if (metadata.Count > MaximumOverlayMetadataEntries)
                {
                    error = $"The metadata object can contain at most {MaximumOverlayMetadataEntries} entries.";
                    return false;
                }

                error = null;
                return true;
            default:
                metadata = new JsonObject();
                error = "The metadataMode argument must be one of: merge, replace, clear.";
                return false;
        }
    }

    private static string NormalizeOverlayMetadataMode(string? metadataMode)
    {
        if (string.Equals(metadataMode, "replace", StringComparison.OrdinalIgnoreCase))
        {
            return "replace";
        }

        if (string.Equals(metadataMode, "clear", StringComparison.OrdinalIgnoreCase))
        {
            return "clear";
        }

        if (!string.IsNullOrWhiteSpace(metadataMode) &&
            !string.Equals(metadataMode, "merge", StringComparison.OrdinalIgnoreCase))
        {
            return metadataMode;
        }

        return "merge";
    }

    private static bool TryCreateOverlayStyle(
        IReadOnlyDictionary<string, string> arguments,
        out OverlayStyle style,
        out string? error)
    {
        style = default!;

        var rawStrokeColor = GetString(arguments, "strokeColor") ?? DefaultOverlayStrokeColor;
        if (!TryParseOverlayColor(rawStrokeColor, allowNone: false, out var strokeColor, out error) || strokeColor == null)
        {
            return false;
        }

        var rawFillColor = GetString(arguments, "fillColor");
        if (!TryParseOverlayColor(rawFillColor, allowNone: true, out var fillColor, out error))
        {
            return false;
        }

        var strokeWidth = GetDouble(arguments, "strokeWidth", defaultValue: 2, minimum: 0, maximum: 128);
        var cornerRadius = GetDouble(arguments, "cornerRadius", defaultValue: 3, minimum: 0, maximum: 256);
        if (strokeWidth <= 0 && fillColor == null)
        {
            error = "The overlay would be invisible because strokeWidth is zero and fillColor is empty.";
            return false;
        }

        style = new OverlayStyle(strokeColor.Value, fillColor, strokeWidth, cornerRadius);
        error = null;
        return true;
    }

    private static bool TryCreateOverlayMetadata(
        IReadOnlyDictionary<string, string> arguments,
        out JsonObject metadata,
        out string? error)
    {
        metadata = new JsonObject();
        var rawMetadata = GetString(arguments, "metadata");
        if (string.IsNullOrWhiteSpace(rawMetadata))
        {
            error = null;
            return true;
        }

        JsonNode? parsedMetadata;
        try
        {
            parsedMetadata = JsonNode.Parse(rawMetadata);
        }
        catch (JsonException exception)
        {
            error = $"The metadata argument must be a JSON object: {exception.Message}";
            return false;
        }

        if (parsedMetadata is not JsonObject metadataObject)
        {
            error = "The metadata argument must be a JSON object.";
            return false;
        }

        if (metadataObject.Count > MaximumOverlayMetadataEntries)
        {
            error = $"The metadata object can contain at most {MaximumOverlayMetadataEntries} entries.";
            return false;
        }

        foreach (var property in metadataObject)
        {
            if (string.IsNullOrWhiteSpace(property.Key) || property.Key.Length > MaximumOverlayMetadataKeyLength)
            {
                error = $"Metadata keys must be non-empty and at most {MaximumOverlayMetadataKeyLength} characters.";
                return false;
            }

            if (property.Value is JsonObject or JsonArray)
            {
                error = "Metadata values must be scalar JSON values.";
                return false;
            }

            metadata[property.Key] = CloneOverlayMetadataValue(property.Value);
        }

        error = null;
        return true;
    }

    private static JsonNode? CloneOverlayMetadataValue(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            return JsonValue.Create(TruncateOverlayString(stringValue));
        }

        return value?.DeepClone();
    }

    private static string TruncateOverlayString(string value)
        => value.Length <= MaximumOverlayMetadataStringLength
            ? value
            : value[..MaximumOverlayMetadataStringLength];

    private static bool TryCreateOverlayRectangles(
        IReadOnlyDictionary<string, string> arguments,
        out IReadOnlyList<OverlayRectangle> rectangles,
        out string? error)
    {
        rectangles = Array.Empty<OverlayRectangle>();

        var nodeId = GetString(arguments, "nodeId");
        var hasCoordinateRectangles = HasCoordinateRectangleArguments(arguments);
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            if (hasCoordinateRectangles)
            {
                error = "Pass either nodeId or rectangle coordinates, not both.";
                return false;
            }

            var label = GetString(arguments, "label");
            if (!TryCreateOverlayRectangleFromNodeId(nodeId, label, out var nodeRectangle, out error))
            {
                return false;
            }

            rectangles = new[] { nodeRectangle };
            error = null;
            return true;
        }

        if (!TryReadRawOverlayRectangles(arguments, out var rawRectangles, out error))
        {
            return false;
        }

        var coordinateSpace = NormalizeOverlayCoordinateSpace(GetString(arguments, "coordinateSpace"));
        var normalizedRectangles = new List<OverlayRectangle>(rawRectangles.Count);
        foreach (var rawRectangle in rawRectangles)
        {
            if (!TryNormalizeOverlayRectangle(rawRectangle, coordinateSpace, out var normalizedRectangle, out error))
            {
                return false;
            }

            normalizedRectangles.Add(normalizedRectangle);
        }

        rectangles = normalizedRectangles;
        error = null;
        return true;
    }

    private static bool HasOverlayGeometryArguments(IReadOnlyDictionary<string, string> arguments)
        => !string.IsNullOrWhiteSpace(GetString(arguments, "nodeId")) ||
           HasCoordinateRectangleArguments(arguments);

    private static bool HasCoordinateRectangleArguments(IReadOnlyDictionary<string, string> arguments)
        => arguments.ContainsKey("rects") ||
           arguments.ContainsKey("x") ||
           arguments.ContainsKey("y") ||
           arguments.ContainsKey("width") ||
           arguments.ContainsKey("height");

    private static bool TryReadRawOverlayRectangles(
        IReadOnlyDictionary<string, string> arguments,
        out IReadOnlyList<OverlayRectangle> rectangles,
        out string? error)
    {
        rectangles = Array.Empty<OverlayRectangle>();
        var rawRects = GetString(arguments, "rects");
        if (!string.IsNullOrWhiteSpace(rawRects))
        {
            return TryReadOverlayRectanglesJson(rawRects, out rectangles, out error);
        }

        var x = GetNullableDouble(arguments, "x");
        var y = GetNullableDouble(arguments, "y");
        var width = GetNullableDouble(arguments, "width");
        var height = GetNullableDouble(arguments, "height");
        if (!x.HasValue || !y.HasValue || !width.HasValue || !height.HasValue)
        {
            error = "Pass nodeId, rects, or all of x/y/width/height.";
            return false;
        }

        if (!TryCreateOverlayRectangle(x.Value, y.Value, width.Value, height.Value, GetString(arguments, "label"), out var rectangle, out error))
        {
            return false;
        }

        rectangles = new[] { rectangle };
        return true;
    }

    private static bool TryReadOverlayRectanglesJson(
        string rawRects,
        out IReadOnlyList<OverlayRectangle> rectangles,
        out string? error)
    {
        rectangles = Array.Empty<OverlayRectangle>();

        JsonNode? parsedRects;
        try
        {
            parsedRects = JsonNode.Parse(rawRects);
        }
        catch (JsonException exception)
        {
            error = $"The rects argument must be a JSON object or array: {exception.Message}";
            return false;
        }

        JsonObject[] rectangleObjects;
        switch (parsedRects)
        {
            case JsonObject singleRectangle:
                rectangleObjects = new[] { singleRectangle };
                break;
            case JsonArray rectangleArray:
                if (rectangleArray.Any(node => node is not JsonObject))
                {
                    error = "Every item in the rects array must be a rectangle object.";
                    return false;
                }

                rectangleObjects = rectangleArray.Cast<JsonObject>().ToArray();
                break;
            default:
                rectangleObjects = Array.Empty<JsonObject>();
                break;
        }

        if (rectangleObjects.Length == 0)
        {
            error = "The rects argument must contain at least one rectangle object.";
            return false;
        }

        if (rectangleObjects.Length > MaximumOverlayRectangles)
        {
            error = $"The rects argument can contain at most {MaximumOverlayRectangles} rectangles.";
            return false;
        }

        var parsedRectangles = new List<OverlayRectangle>(rectangleObjects.Length);
        foreach (var rectangleObject in rectangleObjects)
        {
            if (!TryReadOverlayRectangleJson(rectangleObject, out var rectangle, out error))
            {
                return false;
            }

            parsedRectangles.Add(rectangle);
        }

        rectangles = parsedRectangles;
        error = null;
        return true;
    }

    private static bool TryReadOverlayRectangleJson(
        JsonObject rectangleObject,
        out OverlayRectangle rectangle,
        out string? error)
    {
        rectangle = default;

        if (!TryReadJsonDouble(rectangleObject, "x", out var x, out error) ||
            !TryReadJsonDouble(rectangleObject, "y", out var y, out error) ||
            !TryReadJsonDouble(rectangleObject, "width", out var width, out error) ||
            !TryReadJsonDouble(rectangleObject, "height", out var height, out error))
        {
            return false;
        }

        if (!TryReadOptionalJsonString(rectangleObject, "label", out var label, out error))
        {
            return false;
        }

        return TryCreateOverlayRectangle(x, y, width, height, label, out rectangle, out error);
    }

    private static bool TryReadOptionalJsonString(
        JsonObject jsonObject,
        string propertyName,
        out string? value,
        out string? error)
    {
        value = null;
        if (!jsonObject.TryGetPropertyValue(propertyName, out var node) || node == null)
        {
            error = null;
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            error = null;
            return true;
        }

        error = $"The rectangle property '{propertyName}' must be a string.";
        return false;
    }

    private static bool TryReadJsonDouble(JsonObject jsonObject, string propertyName, out double value, out string? error)
    {
        value = 0;
        if (!jsonObject.TryGetPropertyValue(propertyName, out var node) || node == null)
        {
            error = $"The rectangle property '{propertyName}' is required.";
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<double>(out value))
            {
                error = null;
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var rawValue) &&
                double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = null;
                return true;
            }
        }

        error = $"The rectangle property '{propertyName}' must be a number.";
        return false;
    }

    private static bool TryCreateOverlayRectangle(
        double x,
        double y,
        double width,
        double height,
        string? label,
        out OverlayRectangle rectangle,
        out string? error)
    {
        rectangle = default;

        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            !double.IsFinite(width) ||
            !double.IsFinite(height))
        {
            error = "Overlay rectangle coordinates must be finite numbers.";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = "Overlay rectangle width and height must be greater than zero.";
            return false;
        }

        rectangle = new OverlayRectangle(x, y, width, height, string.IsNullOrWhiteSpace(label) ? null : label.Trim());
        error = null;
        return true;
    }

    private static string NormalizeOverlayCoordinateSpace(string? coordinateSpace)
    {
        if (string.Equals(coordinateSpace, "visualTree", StringComparison.OrdinalIgnoreCase))
        {
            return "visualTree";
        }

        return "window";
    }

    private static bool TryParseOverlayColor(
        string? rawColor,
        bool allowNone,
        out OverlayColor? color,
        out string? error)
    {
        color = null;

        if (string.IsNullOrWhiteSpace(rawColor))
        {
            error = null;
            return allowNone;
        }

        var value = rawColor.Trim();
        if (allowNone &&
            (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)))
        {
            error = null;
            return true;
        }

        value = ResolveNamedOverlayColor(value);
        if (!value.StartsWith('#'))
        {
            error = $"The color '{rawColor}' is not supported. Use #RGB, #ARGB, #RRGGBB, #AARRGGBB, or a common color name.";
            return false;
        }

        var hex = value[1..];
        switch (hex.Length)
        {
            case 3:
                if (TryReadHexNibble(hex[0], out var r3) &&
                    TryReadHexNibble(hex[1], out var g3) &&
                    TryReadHexNibble(hex[2], out var b3))
                {
                    color = new OverlayColor(255, r3 * 17, g3 * 17, b3 * 17);
                    error = null;
                    return true;
                }

                break;
            case 4:
                if (TryReadHexNibble(hex[0], out var a4) &&
                    TryReadHexNibble(hex[1], out var r4) &&
                    TryReadHexNibble(hex[2], out var g4) &&
                    TryReadHexNibble(hex[3], out var b4))
                {
                    color = new OverlayColor(a4 * 17, r4 * 17, g4 * 17, b4 * 17);
                    error = null;
                    return true;
                }

                break;
            case 6:
                if (TryReadHexByte(hex.AsSpan(0, 2), out var r6) &&
                    TryReadHexByte(hex.AsSpan(2, 2), out var g6) &&
                    TryReadHexByte(hex.AsSpan(4, 2), out var b6))
                {
                    color = new OverlayColor(255, r6, g6, b6);
                    error = null;
                    return true;
                }

                break;
            case 8:
                if (TryReadHexByte(hex.AsSpan(0, 2), out var a8) &&
                    TryReadHexByte(hex.AsSpan(2, 2), out var r8) &&
                    TryReadHexByte(hex.AsSpan(4, 2), out var g8) &&
                    TryReadHexByte(hex.AsSpan(6, 2), out var b8))
                {
                    color = new OverlayColor(a8, r8, g8, b8);
                    error = null;
                    return true;
                }

                break;
        }

        error = $"The color '{rawColor}' is not a valid hex color.";
        return false;
    }

    private static string ResolveNamedOverlayColor(string value)
        => value.ToLowerInvariant() switch
        {
            "black" => "#000000",
            "white" => "#FFFFFF",
            "red" => "#FF3B30",
            "orange" => "#FF9500",
            "yellow" => "#FFCC00",
            "green" => "#34C759",
            "blue" => "#007AFF",
            "purple" => "#AF52DE",
            "pink" => "#FF2D55",
            "cyan" => "#32ADE6",
            "gray" or "grey" => "#8E8E93",
            _ => value
        };

    private static bool TryReadHexNibble(char character, out int value)
    {
        value = character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1
        };

        return value >= 0;
    }

    private static bool TryReadHexByte(ReadOnlySpan<char> hex, out int value)
    {
        value = 0;
        if (hex.Length != 2 ||
            !TryReadHexNibble(hex[0], out var high) ||
            !TryReadHexNibble(hex[1], out var low))
        {
            return false;
        }

        value = (high << 4) | low;
        return true;
    }

    private static double GetDouble(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        double defaultValue,
        double minimum,
        double maximum)
    {
        var value = GetNullableDouble(arguments, key);
        return Math.Clamp(value ?? defaultValue, minimum, maximum);
    }

    private static double? GetNullableDouble(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be a number.");
        }

        return parsedValue;
    }

    private static void ScheduleOverlayExpiration(OverlayEntry entry)
    {
        if (entry.DurationMilliseconds <= 0)
        {
            return;
        }

        var delayMilliseconds = entry.ExpiresAtUtc.HasValue
            ? Math.Max(0, (int)Math.Round((entry.ExpiresAtUtc.Value - DateTime.UtcNow).TotalMilliseconds))
            : entry.DurationMilliseconds;
        var timeoutCancellation = new CancellationTokenSource();
        entry.TimeoutCancellation = timeoutCancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMilliseconds, timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            PostOverlayActionToUiThread(() => RemoveOverlayEntryIfCurrent(entry));
        });
    }

    private static void RemoveExpiredOverlays(DateTime utcNow)
    {
        foreach (var entry in overlays.Values.ToArray())
        {
            if (entry.IsExpired(utcNow))
            {
                RemoveOverlayEntry(entry);
            }
        }
    }

    private static void RemoveOverlayEntryIfCurrent(OverlayEntry entry)
    {
        if (overlays.TryGetValue(entry.Id, out var currentEntry) && ReferenceEquals(currentEntry, entry))
        {
            RemoveOverlayEntry(entry);
        }
    }

    private static void RemoveOverlayEntry(OverlayEntry entry)
    {
        if (overlays.TryGetValue(entry.Id, out var currentEntry) && ReferenceEquals(currentEntry, entry))
        {
            overlays.Remove(entry.Id);
        }

        entry.TimeoutCancellation?.Cancel();
        entry.TimeoutCancellation?.Dispose();
        entry.TimeoutCancellation = null;
        RemoveOverlayFromPlatformWindow(entry);
    }

    private static bool OverlayMatchesQuery(OverlayEntry entry, string? metadataKey, string? metadataValue)
    {
        if (string.IsNullOrWhiteSpace(metadataKey))
        {
            return true;
        }

        if (!entry.Metadata.TryGetPropertyValue(metadataKey, out var metadataNode))
        {
            return false;
        }

        if (metadataValue == null)
        {
            return true;
        }

        return string.Equals(GetOverlayMetadataComparisonValue(metadataNode), metadataValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetOverlayMetadataComparisonValue(JsonNode? metadataNode)
    {
        if (metadataNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return metadataNode?.ToJsonString();
    }

    private static JsonObject CreateOverlayResultPayload(OverlayEntry entry)
        => new()
        {
            ["platform"] = CurrentPlatform,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["overlay"] = CreateOverlayJson(entry)
        };

    private static JsonObject CreateOverlayJson(OverlayEntry entry)
    {
        var rects = new JsonArray();
        foreach (var rectangle in entry.Rectangles)
        {
            rects.Add(new JsonObject
            {
                ["x"] = rectangle.X,
                ["y"] = rectangle.Y,
                ["width"] = rectangle.Width,
                ["height"] = rectangle.Height,
                ["label"] = rectangle.Label
            });
        }

        var utcNow = DateTime.UtcNow;
        return new JsonObject
        {
            ["id"] = entry.Id,
            ["platform"] = CurrentPlatform,
            ["createdAtUtc"] = entry.CreatedAtUtc.ToString("O"),
            ["expiresAtUtc"] = entry.ExpiresAtUtc?.ToString("O"),
            ["durationMs"] = entry.DurationMilliseconds,
            ["remainingMs"] = entry.ExpiresAtUtc.HasValue
                ? Math.Max(0, (int)Math.Round((entry.ExpiresAtUtc.Value - utcNow).TotalMilliseconds))
                : null,
            ["transient"] = entry.DurationMilliseconds > 0,
            ["inputTransparent"] = true,
            ["coordinateSpace"] = "window",
            ["style"] = new JsonObject
            {
                ["strokeColor"] = entry.Style.StrokeColor.ToHex(),
                ["fillColor"] = entry.Style.FillColor?.ToHex(),
                ["strokeWidth"] = entry.Style.StrokeWidth,
                ["cornerRadius"] = entry.Style.CornerRadius
            },
            ["rects"] = rects,
            ["metadata"] = entry.Metadata.DeepClone()
        };
    }

    private sealed class OverlayEntry(
        string id,
        IReadOnlyList<OverlayRectangle> rectangles,
        OverlayStyle style,
        JsonObject metadata,
        DateTime createdAtUtc,
        DateTime? expiresAtUtc,
        int durationMilliseconds)
    {
        internal string Id { get; } = id;
        internal IReadOnlyList<OverlayRectangle> Rectangles { get; } = rectangles;
        internal OverlayStyle Style { get; } = style;
        internal JsonObject Metadata { get; } = metadata;
        internal DateTime CreatedAtUtc { get; } = createdAtUtc;
        internal DateTime? ExpiresAtUtc { get; } = expiresAtUtc;
        internal int DurationMilliseconds { get; } = durationMilliseconds;
        internal CancellationTokenSource? TimeoutCancellation { get; set; }
        internal object? PlatformOwner { get; set; }
        internal object? PlatformHandle { get; set; }

        internal bool IsExpired(DateTime utcNow)
            => ExpiresAtUtc.HasValue && utcNow >= ExpiresAtUtc.Value;
    }

    private readonly record struct OverlayRectangle(double X, double Y, double Width, double Height, string? Label);

    private sealed record OverlayStyle(
        OverlayColor StrokeColor,
        OverlayColor? FillColor,
        double StrokeWidth,
        double CornerRadius);

    private readonly record struct OverlayColor(int A, int R, int G, int B)
    {
        internal string ToHex()
            => string.Create(
                CultureInfo.InvariantCulture,
                $"#{A:X2}{R:X2}{G:X2}{B:X2}");
    }

#if ANDROID
    private static bool TryCreateOverlayRectangleFromNodeId(
        string nodeId,
        string? label,
        out OverlayRectangle rectangle,
        out string? error)
    {
        rectangle = default;

        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null)
        {
            error = "No Android root view is currently available.";
            return false;
        }

        var view = FindAndroidOverlayNode(rootView, nodeId);
        if (view == null)
        {
            error = $"The node '{nodeId}' was not found.";
            return false;
        }

        var location = new int[2];
        var rootLocation = new int[2];
        view.GetLocationOnScreen(location);
        rootView.GetLocationOnScreen(rootLocation);

        return TryCreateOverlayRectangle(
            location[0] - rootLocation[0],
            location[1] - rootLocation[1],
            view.Width,
            view.Height,
            label,
            out rectangle,
            out error);
    }

    private static View? FindAndroidOverlayNode(View view, string nodeId)
    {
        if (string.Equals(GetAndroidOverlayNodeId(view), nodeId, StringComparison.Ordinal))
        {
            return view;
        }

        if (view is not ViewGroup group)
        {
            return null;
        }

        for (var index = 0; index < group.ChildCount; index++)
        {
            var child = group.GetChildAt(index);
            if (child == null)
            {
                continue;
            }

            var match = FindAndroidOverlayNode(child, nodeId);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static string GetAndroidOverlayNodeId(View view)
        => view.Handle != IntPtr.Zero ? view.Handle.ToInt64().ToString() : view.GetHashCode().ToString();

    private static bool TryNormalizeOverlayRectangle(
        OverlayRectangle rectangle,
        string coordinateSpace,
        out OverlayRectangle normalizedRectangle,
        out string? error)
    {
        normalizedRectangle = rectangle;

        if (string.Equals(coordinateSpace, "window", StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null)
        {
            error = "No Android root view is currently available.";
            return false;
        }

        var rootLocation = new int[2];
        rootView.GetLocationOnScreen(rootLocation);
        normalizedRectangle = rectangle with
        {
            X = rectangle.X - rootLocation[0],
            Y = rectangle.Y - rootLocation[1]
        };
        error = null;
        return true;
    }

    private static bool TryAttachOverlayToPlatformWindow(OverlayEntry entry, out string? error)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView;
        if (rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            error = "No Android decor view is currently available for overlay rendering.";
            return false;
        }

        var overlay = rootView.Overlay;
        if (overlay == null)
        {
            error = "The Android decor view does not expose an overlay surface.";
            return false;
        }

        var drawable = new AndroidOverlayDrawable(entry);
        drawable.SetBounds(0, 0, rootView.Width, rootView.Height);
        overlay.Add(drawable);
        rootView.Invalidate();

        entry.PlatformOwner = rootView;
        entry.PlatformHandle = drawable;
        error = null;
        return true;
    }

    private static void RemoveOverlayFromPlatformWindow(OverlayEntry entry)
    {
        if (entry.PlatformOwner is View rootView && entry.PlatformHandle is Drawable drawable)
        {
            rootView.Overlay?.Remove(drawable);
            rootView.Invalidate();
            drawable.Dispose();
        }

        entry.PlatformOwner = null;
        entry.PlatformHandle = null;
    }

    private static void PostOverlayActionToUiThread(Action action)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        if (activity != null)
        {
            activity.RunOnUiThread(action);
            return;
        }

        new Handler(Looper.MainLooper!).Post(action);
    }

    private sealed class AndroidOverlayDrawable : Drawable
    {
        private readonly OverlayEntry entry;
        private readonly Paint strokePaint;
        private readonly Paint? fillPaint;

        internal AndroidOverlayDrawable(OverlayEntry entry)
        {
            this.entry = entry;
            strokePaint = new Paint(PaintFlags.AntiAlias);
            strokePaint.SetStyle(Paint.Style.Stroke);
            strokePaint.StrokeWidth = (float)entry.Style.StrokeWidth;
            strokePaint.SetARGB(
                entry.Style.StrokeColor.A,
                entry.Style.StrokeColor.R,
                entry.Style.StrokeColor.G,
                entry.Style.StrokeColor.B);

            if (entry.Style.FillColor is { } fillColor)
            {
                fillPaint = new Paint(PaintFlags.AntiAlias);
                fillPaint.SetStyle(Paint.Style.Fill);
                fillPaint.SetARGB(fillColor.A, fillColor.R, fillColor.G, fillColor.B);
            }
        }

        public override int Opacity => (int)Format.Translucent;

        public override void Draw(Canvas canvas)
        {
            foreach (var rectangle in entry.Rectangles)
            {
                using var rect = new RectF(
                    (float)rectangle.X,
                    (float)rectangle.Y,
                    (float)(rectangle.X + rectangle.Width),
                    (float)(rectangle.Y + rectangle.Height));
                var cornerRadius = (float)entry.Style.CornerRadius;

                if (fillPaint != null)
                {
                    canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, fillPaint);
                }

                if (entry.Style.StrokeWidth > 0)
                {
                    canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, strokePaint);
                }
            }
        }

        public override void SetAlpha(int alpha)
        {
            strokePaint.Alpha = alpha;
            if (fillPaint != null)
            {
                fillPaint.Alpha = alpha;
            }
        }

        public override void SetColorFilter(ColorFilter? colorFilter)
        {
            strokePaint.SetColorFilter(colorFilter);
            fillPaint?.SetColorFilter(colorFilter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                strokePaint.Dispose();
                fillPaint?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
#elif IOS || MACCATALYST
    private static bool TryCreateOverlayRectangleFromNodeId(
        string nodeId,
        string? label,
        out OverlayRectangle rectangle,
        out string? error)
    {
        rectangle = default;

        var window = GetActiveWindow();
        if (window == null)
        {
            error = "No active UIWindow is available.";
            return false;
        }

        var view = FindAppleOverlayNode(window, nodeId);
        if (view == null)
        {
            error = $"The node '{nodeId}' was not found.";
            return false;
        }

        var frame = view.ConvertRectToView(view.Bounds, window);
        return TryCreateOverlayRectangle(
            (double)frame.X,
            (double)frame.Y,
            (double)frame.Width,
            (double)frame.Height,
            label,
            out rectangle,
            out error);
    }

    private static UIView? FindAppleOverlayNode(UIView view, string nodeId)
    {
        if (string.Equals(GetAppleOverlayNodeId(view), nodeId, StringComparison.Ordinal))
        {
            return view;
        }

        foreach (var child in view.Subviews)
        {
            var match = FindAppleOverlayNode(child, nodeId);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static string GetAppleOverlayNodeId(UIView view)
        => !string.IsNullOrWhiteSpace(view.Handle.ToString()) ? view.Handle.ToString() : view.GetHashCode().ToString();

    private static bool TryNormalizeOverlayRectangle(
        OverlayRectangle rectangle,
        string coordinateSpace,
        out OverlayRectangle normalizedRectangle,
        out string? error)
    {
        normalizedRectangle = rectangle;
        error = null;
        return true;
    }

    private static bool TryAttachOverlayToPlatformWindow(OverlayEntry entry, out string? error)
    {
        var window = GetActiveWindow();
        if (window == null || window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
        {
            error = "No active UIWindow is available for overlay rendering.";
            return false;
        }

        var overlayView = new AppleOverlayView(entry, window.Bounds);
        window.AddSubview(overlayView);
        window.BringSubviewToFront(overlayView);

        entry.PlatformOwner = window;
        entry.PlatformHandle = overlayView;
        error = null;
        return true;
    }

    private static void RemoveOverlayFromPlatformWindow(OverlayEntry entry)
    {
        if (entry.PlatformHandle is UIView overlayView)
        {
            overlayView.RemoveFromSuperview();
            overlayView.Dispose();
        }

        entry.PlatformOwner = null;
        entry.PlatformHandle = null;
    }

    private static void PostOverlayActionToUiThread(Action action)
        => UIApplication.SharedApplication.BeginInvokeOnMainThread(action);

    private sealed class AppleOverlayView : UIView
    {
        private readonly OverlayEntry entry;

        internal AppleOverlayView(OverlayEntry entry, CGRect frame)
            : base(frame)
        {
            this.entry = entry;
            BackgroundColor = UIColor.Clear;
            Opaque = false;
            UserInteractionEnabled = false;
            AccessibilityElementsHidden = true;
            IsAccessibilityElement = false;
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        }

        public override UIView? HitTest(CGPoint point, UIEvent? uievent)
            => null;

        public override bool PointInside(CGPoint point, UIEvent? uievent)
            => false;

        public override void Draw(CGRect rect)
        {
            foreach (var rectangle in entry.Rectangles)
            {
                var cgRect = new CGRect(
                    rectangle.X,
                    rectangle.Y,
                    rectangle.Width,
                    rectangle.Height);
                using var path = UIBezierPath.FromRoundedRect(cgRect, (nfloat)entry.Style.CornerRadius);

                if (entry.Style.FillColor is { } fillColor)
                {
                    using var nativeFillColor = CreateNativeColor(fillColor);
                    nativeFillColor.SetFill();
                    path.Fill();
                }

                if (entry.Style.StrokeWidth > 0)
                {
                    using var nativeStrokeColor = CreateNativeColor(entry.Style.StrokeColor);
                    nativeStrokeColor.SetStroke();
                    path.LineWidth = (nfloat)entry.Style.StrokeWidth;
                    path.Stroke();
                }
            }
        }

        private static UIColor CreateNativeColor(OverlayColor color)
            => UIColor.FromRGBA(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
    }
#else
    private static bool TryCreateOverlayRectangleFromNodeId(
        string nodeId,
        string? label,
        out OverlayRectangle rectangle,
        out string? error)
    {
        rectangle = default;
        error = "Overlay rendering is not available on this platform.";
        return false;
    }

    private static bool TryNormalizeOverlayRectangle(
        OverlayRectangle rectangle,
        string coordinateSpace,
        out OverlayRectangle normalizedRectangle,
        out string? error)
    {
        normalizedRectangle = rectangle;
        error = null;
        return true;
    }

    private static bool TryAttachOverlayToPlatformWindow(OverlayEntry entry, out string? error)
    {
        error = "Overlay rendering is not available on this platform.";
        return false;
    }

    private static void RemoveOverlayFromPlatformWindow(OverlayEntry entry)
    {
    }

    private static void PostOverlayActionToUiThread(Action action)
        => action();
#endif
}
