using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Ansight;

/// <summary>
/// Extension methods for wiring Ansight into a MAUI application builder.
/// </summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Set's up the <see cref="MauiAppBuilder"/> to initialise the <see cref="Runtime"/> and registers required fonts and dependencies.
    /// Does not activate the memory tracking, please use <see cref="Runtime.Activate"/> to start tracking memory usage.
    /// </summary>
    public static MauiAppBuilder UseAnsight(this MauiAppBuilder builder, Options? ansightOptions = null)
    {
        var ansightOptionsBuilder = ansightOptions == null
            ? Options.CreateBuilder()
            : Options.CreateBuilder(ansightOptions);

        if (ansightOptions?.PresentationWindowProvider == null)
        {
            ansightOptionsBuilder.WithMauiWindowProvider();
        }

        ansightOptions = ansightOptionsBuilder.Build();

#if ANDROID
        if (ansightOptions.PresentationWindowProvider == null)
        {
            throw new InvalidOperationException("Options.PresentationWindowProvider is required on Android. Call WithMauiWindowProvider or WithPresentationWindowProvider.");
        }
#endif

        if (!Runtime.IsInitialized)
        {
            Runtime.Initialize(ansightOptions);
        }

        builder.Services.AddSingleton<IRuntime>(_ => Runtime.Instance);
        builder.Services.AddSingleton<IDataSink>(_ => Runtime.Instance.DataSink);
        
        return builder.UseSkiaSharp();
    }

    /// <summary>
    /// Set's up the <see cref="MauiAppBuilder"/> to initialise the <see cref="Runtime"/> and registers required fonts and dependencies.
    /// Immediately activates Ansights memory tracking.
    /// </summary>
    public static MauiAppBuilder UseAnsightAndActivate(this MauiAppBuilder builder, Options? ansightOptions = null)
    {
        builder = builder.UseAnsight(ansightOptions);
        
        Runtime.Activate();
        
        return builder;
    }
}
