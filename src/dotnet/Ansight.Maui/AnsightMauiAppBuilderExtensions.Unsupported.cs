#if !ANDROID && !IOS && !MACCATALYST
namespace Ansight.Maui;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

/// <summary>Source-compatible no-op registration on platforms without Ansight MAUI capture.</summary>
public static class AnsightMauiAppBuilderExtensions
{
    /// <summary>Returns the builder unchanged. Does not invoke configure or initialize Ansight.</summary>
    public static MauiAppBuilder UseAnsight(this MauiAppBuilder builder, Action<Options.OptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder;
    }

    /// <summary>Returns the builder unchanged. Does not invoke configure or initialize Ansight.</summary>
    public static MauiAppBuilder UseAnsight<TApplication>(this MauiAppBuilder builder, Action<Options.OptionsBuilder>? configure = null)
        where TApplication : Application
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder;
    }

    /// <summary>Returns the builder unchanged without applying runtime options.</summary>
    public static MauiAppBuilder UseAnsight(this MauiAppBuilder builder, Options options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        return builder;
    }
}
#endif
