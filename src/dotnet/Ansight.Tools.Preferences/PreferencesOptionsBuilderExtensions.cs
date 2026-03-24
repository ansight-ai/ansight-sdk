namespace Ansight.Tools.Preferences;

using System;

public static class PreferencesOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithPreferencesTools(this Options.OptionsBuilder builder)
        => builder.WithPreferencesTools(static _ => { });

    public static Options.OptionsBuilder WithPreferencesTools(
        this Options.OptionsBuilder builder,
        Action<PreferencesToolsOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = PreferencesToolsOptions.CreateBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        return builder.AddTools(new ITool[]
        {
            new ListPreferenceKeysTool(options),
            new GetPreferenceValueTool(options),
            new SetPreferenceValueTool(options),
            new RemovePreferenceKeyTool(options)
        });
    }
}
