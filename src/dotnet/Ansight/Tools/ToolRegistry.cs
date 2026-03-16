namespace Ansight.Tools;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class ToolRegistry : IReadOnlyCollection<ITool>
{
    private readonly IReadOnlyList<ITool> tools;
    private readonly IReadOnlyDictionary<string, ITool> toolsById;

    public static ToolRegistry Empty { get; } = new(Array.Empty<ITool>());

    public ToolRegistry()
        : this(Array.Empty<ITool>())
    {
    }

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

    public int Count => tools.Count;

    public ToolRegistry Add(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return AddRange(new[] { tool });
    }

    public ToolRegistry AddRange(IEnumerable<ITool> additionalTools)
    {
        ArgumentNullException.ThrowIfNull(additionalTools);
        return new ToolRegistry(tools.Concat(additionalTools));
    }

    public bool Contains(string toolId)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolId);
        return toolsById.ContainsKey(toolId);
    }

    public IReadOnlyList<ITool> GetByCategory(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        return tools.Where(tool => string.Equals(tool.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public bool TryGet(string toolId, out ITool? tool)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolId);
        return toolsById.TryGetValue(toolId, out tool);
    }

    public IReadOnlyList<ToolDefinition> GetDefinitions() => tools.Select(tool => tool.Definition).ToList();

    public ToolProtocolBridge CreateBridge(ToolGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return new ToolProtocolBridge(this, guard);
    }

    public void Validate()
    {
        _ = new ToolRegistry(tools);

        foreach (var tool in tools)
        {
            tool.ArgumentsSchema.Validate();
            tool.ResultSchema.Validate();
        }
    }

    public IEnumerator<ITool> GetEnumerator() => tools.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
