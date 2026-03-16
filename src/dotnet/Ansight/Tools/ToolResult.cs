namespace Ansight.Tools;

using System.Text.Json.Nodes;

public sealed class ToolResult
{
    private ToolResult(bool isSuccess, string? message, string? errorCode, JsonNode? payload)
    {
        IsSuccess = isSuccess;
        Message = message;
        ErrorCode = errorCode;
        Payload = payload;
    }

    public bool IsSuccess { get; }

    public string? Message { get; }

    public string? ErrorCode { get; }

    public JsonNode? Payload { get; }

    public static ToolResult Success(JsonNode? payload = null, string? message = null)
        => new(true, message, null, payload);

    public static ToolResult Failure(string message, string? errorCode = null, JsonNode? payload = null)
        => new(false, message, errorCode, payload);
}
