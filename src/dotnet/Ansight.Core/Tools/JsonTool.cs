namespace Ansight.Tools;

/// <summary>
/// A JSON tool backed by a definition and an asynchronous handler.
/// Uses the same policy, availability, and schema validation as other <see cref="IJsonTool"/> implementations.
/// </summary>
public sealed class JsonTool : IJsonTool
{
    private readonly Func<ToolInvocation, CancellationToken, Task<ToolResult>> executeAsync;
    private readonly Func<ToolAvailabilityContext, ValueTask<ToolAvailability>>? getAvailabilityAsync;

    private JsonTool(
        ToolDefinition definition,
        Func<ToolInvocation, CancellationToken, Task<ToolResult>> executeAsync,
        Func<ToolAvailabilityContext, ValueTask<ToolAvailability>>? getAvailabilityAsync)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executeAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        ArgumentNullException.ThrowIfNull(definition.ArgumentsSchema);
        ArgumentNullException.ThrowIfNull(definition.ResultSchema);
        if (!Enum.IsDefined(definition.Policy)) throw new ArgumentOutOfRangeException(nameof(definition));

        Definition = definition with
        {
            PrerequisiteToolIds = Array.AsReadOnly(definition.PrerequisiteToolIds?.ToArray() ?? [])
        };
        this.executeAsync = executeAsync;
        this.getAvailabilityAsync = getAvailabilityAsync;
    }

    /// <summary>
    /// Creates a tool without a dedicated implementation class. Handlers run on the calling
    /// transport's context; dispatch UI or engine work to its owning thread explicitly.
    /// </summary>
    public static JsonTool Create(
        ToolDefinition definition,
        Func<ToolInvocation, CancellationToken, Task<ToolResult>> executeAsync,
        Func<ToolAvailabilityContext, ValueTask<ToolAvailability>>? getAvailabilityAsync = null)
        => new(definition, executeAsync, getAvailabilityAsync);

    public ToolDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Category => Definition.Category;
    public string Keywords => Definition.Keywords;
    public ToolPolicy Policy => Definition.Policy;
    public ToolSchema ArgumentsSchema => Definition.ArgumentsSchema;
    public ToolSchema ResultSchema => Definition.ResultSchema;
    public IReadOnlyList<string> PrerequisiteToolIds => Definition.PrerequisiteToolIds!;

    public ValueTask<ToolAvailability> GetAvailabilityAsync(ToolAvailabilityContext context)
        => getAvailabilityAsync?.Invoke(context) ?? ValueTask.FromResult(ToolAvailability.Available);

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        return executeAsync(invocation, cancellationToken);
    }
}
