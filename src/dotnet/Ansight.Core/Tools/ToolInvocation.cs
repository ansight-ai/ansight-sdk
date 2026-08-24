namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// JSON-native invocation delivered to an <see cref="IJsonTool"/>.
/// </summary>
public sealed record ToolInvocation(
    JsonObject Arguments,
    ToolInvocationContext Context);

/// <summary>
/// Correlation and session context for one tool invocation.
/// </summary>
public sealed record ToolInvocationContext(
    string RequestId,
    string? SessionId,
    string? CallId);
