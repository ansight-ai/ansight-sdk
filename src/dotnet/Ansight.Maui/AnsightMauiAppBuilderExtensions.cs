#if ANDROID || IOS || MACCATALYST
namespace Ansight.Maui;

using Ansight.Telemetry.Data;
using Ansight.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

/// <summary>
/// Provides Ansight extensions for <see cref="MauiAppBuilder" />.
/// </summary>
public static class AnsightMauiAppBuilderExtensions
{
    /// <summary>
    /// Initializes and activates Ansight using the default .NET MAUI all-in-one configuration.
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="configure">
    /// An optional callback that customizes the existing Ansight options builder after runtime defaults and default tool access,
    /// but before default tool registration.
    /// Registering a tool suite inside this callback replaces the default all-in-one registration for that suite.
    /// </param>
    /// <returns>The current MAUI app builder.</returns>
    public static MauiAppBuilder UseAnsight(
        this MauiAppBuilder builder,
        Action<Options.OptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = Options.CreateBuilder().WithAnsightMaui(options =>
        {
            configure?.Invoke(options);
        });

        return builder.UseAnsight(optionsBuilder.Build());
    }

    /// <summary>
    /// Initializes and activates Ansight using the default .NET MAUI all-in-one configuration and the app assembly for bundled host connection resources.
    /// </summary>
    /// <typeparam name="TApplication">The MAUI application type used to discover bundled host connection resources.</typeparam>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="configure">
    /// An optional callback that customizes the existing Ansight options builder after runtime defaults and default tool access,
    /// but before default tool registration.
    /// Registering a tool suite inside this callback replaces the default all-in-one registration for that suite.
    /// </param>
    /// <returns>The current MAUI app builder.</returns>
    public static MauiAppBuilder UseAnsight<TApplication>(
        this MauiAppBuilder builder,
        Action<Options.OptionsBuilder>? configure = null)
        where TApplication : Application
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = Options.CreateBuilder().WithAnsightMaui(options =>
        {
            options.WithBundledHostConnection(typeof(TApplication).Assembly);
            configure?.Invoke(options);
        });

        return builder.UseAnsight(optionsBuilder.Build());
    }

    /// <summary>
    /// Initializes and activates Ansight using prebuilt runtime options.
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="options">The prebuilt Ansight runtime options.</param>
    /// <returns>The current MAUI app builder.</returns>
    public static MauiAppBuilder UseAnsight(
        this MauiAppBuilder builder,
        Options options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        if (!Runtime.IsInitialized)
        {
            Runtime.InitializeAndActivate(options);
        }
        else if (!Runtime.IsActive)
        {
            Runtime.Activate();
        }

        builder.Services.AddSingleton<IRuntime>(_ => Runtime.Instance);
        builder.Services.AddSingleton<IDataSink>(_ => Runtime.Instance.DataSink);
        builder.Services.AddSingleton<IHostConnection>(_ => Runtime.HostConnection);
        builder.Services.AddSingleton<ToolProtocolBridge>(_ => Runtime.ToolBridge);

        return builder;
    }
}
#endif
