namespace Ansight.Tools.SecureStorage;

using System;

public static class SecureStorageOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithSecureStorageTools(this Options.OptionsBuilder builder)
        => builder.WithSecureStorageTools(static _ => { });

    public static Options.OptionsBuilder WithSecureStorageTools(
        this Options.OptionsBuilder builder,
        Action<SecureStorageToolsOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = SecureStorageToolsOptions.CreateBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        return builder.AddTools(new ITool[]
        {
            new GetSecureStorageValueTool(options),
            new SetSecureStorageValueTool(options),
            new RemoveSecureStorageKeyTool(options)
        });
    }
}
