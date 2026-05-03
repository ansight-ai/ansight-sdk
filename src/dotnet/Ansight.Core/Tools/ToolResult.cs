namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Structured result returned by a tool execution.
/// </summary>
public sealed class ToolResult
{
    private ToolResult(bool isSuccess, string? message, string? errorCode, JsonNode? payload)
    {
        IsSuccess = isSuccess;
        Message = message;
        ErrorCode = errorCode;
        Payload = payload;
    }

    /// <summary>
    /// Indicates whether the tool execution succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Optional human-readable result or error message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Optional machine-readable error code for failed executions.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Optional JSON payload returned by the tool.
    /// </summary>
    public JsonNode? Payload { get; }

    /// <summary>
    /// Creates a successful tool result.
    /// </summary>
    /// <param name="payload">Optional JSON payload returned by the tool.</param>
    /// <param name="message">Optional human-readable success message.</param>
    /// <returns>A successful tool result.</returns>
    public static ToolResult Success(JsonNode? payload = null, string? message = null)
        => new(true, message, null, payload);

    /// <summary>
    /// Creates a failed tool result.
    /// </summary>
    /// <param name="message">Human-readable failure message.</param>
    /// <param name="errorCode">Optional machine-readable failure code.</param>
    /// <param name="payload">Optional JSON payload describing the failure in more detail.</param>
    /// <returns>A failed tool result.</returns>
    public static ToolResult Failure(string message, string? errorCode = null, JsonNode? payload = null)
        => new(false, message, errorCode, payload);
}
