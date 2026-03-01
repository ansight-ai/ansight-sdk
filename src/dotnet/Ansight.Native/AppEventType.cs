using System.Collections.Generic;

namespace Ansight;

/// <summary>
/// Built-in event categories supported by Ansight.
/// </summary>
public enum AppEventType
{
    Event,
    Debug,
    Info,
    Warning,
    Error,
    Exception,
    Gc,
    Navigation
}

/// <summary>
/// Provides a compact symbol per <see cref="AppEventType"/> for rendering events.
/// </summary>
public static class AppEventLegend
{
    public static readonly IReadOnlyDictionary<AppEventType, string?> Symbols =
        new Dictionary<AppEventType, string?>
        {
            { AppEventType.Event,      "*" },
            { AppEventType.Debug,      "#" },
            { AppEventType.Info,       "i" },
            { AppEventType.Warning,    "w" },
            { AppEventType.Error,      "e" },
            { AppEventType.Exception,  "!" },
            { AppEventType.Gc,         "g" },
            { AppEventType.Navigation, ">" }
        };

    public static string? GetSymbol(AppEventType type)
        => Symbols.GetValueOrDefault(type, "?");

    public static bool TryGetSymbol(AppEventType type, out string? symbol)
        => Symbols.TryGetValue(type, out symbol);
}
