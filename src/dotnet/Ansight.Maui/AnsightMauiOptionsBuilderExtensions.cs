namespace Ansight.Maui;

using Ansight.Tools.Maui;

/// <summary>
/// Provides all-in-one Ansight extensions for .NET MAUI apps.
/// </summary>
public static class AnsightMauiOptionsBuilderExtensions
{
    /// <summary>
    /// Applies the default Ansight configuration and registers the MAUI remote tools.
    /// </summary>
    /// <param name="builder">The Ansight options builder.</param>
    /// <returns>The current options builder.</returns>
    public static Options.OptionsBuilder WithAnsightMaui(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnsightMaui(static _ => { });
    }

    /// <summary>
    /// Applies the default MAUI Ansight configuration, runs a callback against the same <see cref="Options.OptionsBuilder" />
    /// before the default tool suites are registered, and registers MAUI tools when they have not already been registered.
    /// </summary>
    /// <param name="builder">The Ansight options builder.</param>
    /// <param name="configure">
    /// A callback that customizes the existing Ansight options builder after runtime defaults and default tool access,
    /// but before default tool registration.
    /// Registering a tool suite inside this callback replaces the default all-in-one registration for that suite.
    /// </param>
    /// <returns>The current options builder.</returns>
    public static Options.OptionsBuilder WithAnsightMaui(
        this Options.OptionsBuilder builder,
        Action<Options.OptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder = builder.WithAnsight(configure);

        if (!ContainsAnyTool(builder, mauiSuiteToolIds))
        {
            builder = builder.WithMauiTools();
        }

#if ANDROID
        builder = builder.WithPlatformPairing(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
#endif

        return builder;
    }

    private static bool ContainsAnyTool(Options.OptionsBuilder builder, IEnumerable<string> toolIds)
    {
        foreach (var toolId in toolIds)
        {
            if (builder.ContainsTool(toolId))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] mauiSuiteToolIds =
    [
        MauiToolIds.GetCurrentPage,
        MauiToolIds.GetVisualTree,
        MauiToolIds.FindElements,
        MauiToolIds.GetElement,
        MauiToolIds.GetBindableProperty,
        MauiToolIds.SetBindableProperty,
        MauiToolIds.ClearBindableProperty,
        MauiToolIds.InflateXaml,
        MauiToolIds.AddElement,
        MauiToolIds.RemoveElement,
        MauiToolIds.SetAppTheme,
        MauiToolIds.GetBindingContext,
        MauiToolIds.GetBindings,
        MauiToolIds.GetResourceState,
        MauiToolIds.GetNavigationState,
        MauiToolIds.InvokeElementAction,
        MauiToolIds.WaitForUi,
        MauiToolIds.GetLayoutDiagnostics,
        MauiToolIds.GetHandlerDiagnostics,
        MauiToolIds.InvokeBindingContextCommand,
        MauiToolIds.SetBindingContextProperty
    ];
}
