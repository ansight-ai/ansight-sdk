using System.Runtime.CompilerServices;
using Ansight.Tools.Avalonia;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Ansight.Avalonia;

public static class AnsightAvaloniaAppBuilderExtensions
{
    private static readonly ConditionalWeakTable<AppBuilder, Registration> registrations = new();

    public static AppBuilder UseAnsight<TApplication>(
        this AppBuilder builder,
        Action<Options.OptionsBuilder>? configure = null)
        where TApplication : global::Avalonia.Application
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (registrations.TryGetValue(builder, out _))
        {
            throw new InvalidOperationException("Ansight has already been registered on this Avalonia AppBuilder.");
        }

        var registration = new Registration(typeof(TApplication), configure);
        registrations.Add(builder, registration);
        registration.StartRenderingMetrics();

        return builder.AfterSetup(registration.Initialize);
    }

    private sealed class Registration
    {
        private readonly Type applicationType;
        private readonly Action<Options.OptionsBuilder>? configure;
        private readonly AvaloniaRenderingMetrics renderingMetrics;

        internal Registration(Type applicationType, Action<Options.OptionsBuilder>? configure)
        {
            this.applicationType = applicationType;
            this.configure = configure;
            renderingMetrics = new AvaloniaRenderingMetrics();
        }

        internal void StartRenderingMetrics()
        {
            renderingMetrics.Start();
        }

        internal void Initialize(AppBuilder builder)
        {
            if (Runtime.IsInitialized)
            {
                renderingMetrics.Dispose();
                throw new InvalidOperationException("UseAnsight must initialize the runtime so Avalonia providers and tools are registered before activation.");
            }

            var session = AvaloniaInspectionSession.Default;
            session.StartTrackingRoots();

            var options = Options.CreateBuilder().WithAnsightSdk(optionsBuilder =>
            {
                optionsBuilder.WithBundledHostConnection(applicationType.Assembly);
                configure?.Invoke(optionsBuilder);
                optionsBuilder.WithAvaloniaTools(session);

                foreach (var property in AvaloniaSessionProperties.CreateValues(builder, applicationType))
                {
                    optionsBuilder.RegisterCustomProperty(AvaloniaSessionProperties.GroupName, property.Key, property.Value);
                }

                foreach (var channel in renderingMetrics.Channels)
                {
                    optionsBuilder.AddAdditionalChannel(channel);
                }
            }).Build();

            var conflictingChannels = options.AdditionalChannels.GroupBy(channel => channel.Id)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();

            if (conflictingChannels.Length > 0)
            {
                renderingMetrics.Dispose();
                throw new InvalidOperationException($"Duplicate telemetry channel IDs: {string.Join(", ", conflictingChannels)}. Avalonia rendering uses IDs 40–61.");
            }

            Runtime.InitializeAndActivate(options);
            Runtime.SetAppLifecycleState(AppLifecycleState.Foreground);

            if (builder.Instance?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
            {
                lifetime.Activated += Activated;
                lifetime.Deactivated += Deactivated;
            }

            if (builder.Instance?.ApplicationLifetime is IControlledApplicationLifetime controlled)
            {
                controlled.Exit += Exited;
            }
        }

        private static void Activated(object? sender, ActivatedEventArgs eventArgs)
        {
            Runtime.SetAppLifecycleState(AppLifecycleState.Foreground);
        }

        private static void Deactivated(object? sender, ActivatedEventArgs eventArgs)
        {
            Runtime.SetAppLifecycleState(AppLifecycleState.Background);
        }

        private void Exited(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
        {
            renderingMetrics.Dispose();
            Runtime.Deactivate();
            AvaloniaInspectionSession.Default.Dispose();
        }
    }
}
