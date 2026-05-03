namespace Ansight.Tools;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Immutable collection of registered tools with lookup, validation, and bridge-construction helpers.
/// </summary>
public sealed class ToolRegistry : IReadOnlyCollection<ITool>
{
    private readonly IReadOnlyList<ITool> tools;
    private readonly IReadOnlyDictionary<string, ITool> toolsById;

    /// <summary>
    /// Empty tool registry instance.
    /// </summary>
    public static ToolRegistry Empty { get; } = new(Array.Empty<ITool>());

    /// <summary>
    /// Creates an empty tool registry.
    /// </summary>
    public ToolRegistry()
        : this(Array.Empty<ITool>())
    {
    }

    /// <summary>
    /// Creates a tool registry from the supplied tools.
    /// </summary>
    /// <param name="tools">Tools to register.</param>
    public ToolRegistry(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var orderedTools = new List<ITool>();
        var indexedTools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);

            if (string.IsNullOrWhiteSpace(tool.Id))
            {
                throw new InvalidOperationException("Tool ids must be non-empty.");
            }

            if (indexedTools.ContainsKey(tool.Id))
            {
                throw new InvalidOperationException($"A tool with id '{tool.Id}' has already been registered.");
            }

            orderedTools.Add(tool);
            indexedTools.Add(tool.Id, tool);
        }

        this.tools = orderedTools;
        toolsById = indexedTools;
    }

    /// <summary>
    /// Number of registered tools.
    /// </summary>
    public int Count => tools.Count;

    /// <summary>
    /// Returns a new registry that includes the supplied tool.
    /// </summary>
    /// <param name="tool">Tool to add.</param>
    /// <returns>A new registry instance containing the existing tools plus the supplied tool.</returns>
    public ToolRegistry Add(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AddRange(new[] { tool });
    }

    /// <summary>
    /// Returns a new registry that includes the supplied tools.
    /// </summary>
    /// <param name="additionalTools">Tools to add.</param>
    /// <returns>A new registry instance containing the existing tools plus the supplied tools.</returns>
    public ToolRegistry AddRange(IEnumerable<ITool> additionalTools)
    {
        ArgumentNullException.ThrowIfNull(additionalTools);
        return new ToolRegistry(tools.Concat(additionalTools));
    }

    /// <summary>
    /// Determines whether a tool with the supplied id is registered.
    /// </summary>
    /// <param name="toolId">Tool id to look up.</param>
    /// <returns><see langword="true"/> when a tool with the supplied id is registered; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string toolId)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolId);
        return toolsById.ContainsKey(toolId);
    }

    /// <summary>
    /// Returns all tools in the supplied category.
    /// </summary>
    /// <param name="category">Category name to match.</param>
    /// <returns>Registered tools in the supplied category.</returns>
    public IReadOnlyList<ITool> GetByCategory(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        return tools.Where(tool => string.Equals(tool.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Attempts to resolve a tool by id.
    /// </summary>
    /// <param name="toolId">Tool id to resolve.</param>
    /// <param name="tool">Resolved tool when found.</param>
    /// <returns><see langword="true"/> when the tool was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(string toolId, out ITool? tool)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolId);
        return toolsById.TryGetValue(toolId, out tool);
    }

    /// <summary>
    /// Returns the serializable tool definitions for every registered tool.
    /// </summary>
    /// <returns>Registered tool definitions.</returns>
    public IReadOnlyList<ToolDefinition> GetDefinitions() => tools.Select(tool => tool.Definition).ToList();

    /// <summary>
    /// Creates a tool protocol bridge over this registry using the supplied guard policy.
    /// </summary>
    /// <param name="guard">Guard policy to apply to discovery and execution.</param>
    /// <returns>A tool protocol bridge for this registry.</returns>
    public ToolProtocolBridge CreateBridge(ToolGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return new ToolProtocolBridge(this, guard);
    }

    /// <summary>
    /// Validates tool ids and declared schemas across the registry.
    /// </summary>
    public void Validate()
    {
        _ = new ToolRegistry(tools);

        foreach (var tool in tools)
        {
            tool.ArgumentsSchema.Validate();
            tool.ResultSchema.Validate();
        }
    }

    /// <summary>
    /// Returns an enumerator over the registered tools.
    /// </summary>
    /// <returns>Enumerator over the registered tools.</returns>
    public IEnumerator<ITool> GetEnumerator() => tools.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
