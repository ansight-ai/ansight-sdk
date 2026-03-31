namespace Ansight.Tools.Reflection;

public static class ReflectionOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithReflectionTools(this Options.OptionsBuilder builder)
        => builder.WithReflectionTools(static _ => { });

    public static Options.OptionsBuilder WithReflectionTools(
        this Options.OptionsBuilder builder,
        Action<ReflectionToolsOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = ReflectionToolsOptions.CreateBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        return builder.AddTools(new ITool[]
        {
            new ListReflectionRootsTool(options),
            new InspectObjectTool(options),
            new DescribeTypeTool(options),
            new SetMemberValueTool(options),
            new InvokeMethodTool(options)
        });
    }
}
