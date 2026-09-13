namespace Ansight.Tools.Clipboard;

using System.Text;
using System.Text.Json.Nodes;

internal interface IClipboardBackend
{
    string? GetText();
    bool HasText();
    void SetText(string text);
    void Clear();
}

internal sealed class ClipboardException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Session-discovered operations on the device's system clipboard.</summary>
public abstract class ClipboardTool : IJsonTool
{
    internal const int MaximumTextBytes = 65_536;
    private readonly IClipboardBackend? backend;

    internal ClipboardTool(string id, string name, string description, ToolPolicy policy, IClipboardBackend? backend)
    {
        Id = id;
        Name = name;
        Description = description;
        Policy = policy;
        this.backend = backend;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public ToolPolicy Policy { get; }
    public string Category => "clipboard";
    public string Keywords => "clipboard copy paste text";
    public ToolSchema ArgumentsSchema => ClipboardToolSchemas.Arguments(Id);
    public ToolSchema ResultSchema => ClipboardToolSchemas.Result(Id);

    public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = new Dictionary<string, string>();
        foreach (var property in invocation.Arguments)
        {
            if (property.Value is not JsonValue value || !value.TryGetValue<string>(out var text))
                return Task.FromResult(ToolResult.Failure("Clipboard arguments must be strings.", errorCode: "clipboard_invalid_arguments"));
            arguments[property.Key] = text;
        }
        return Execute(arguments);
    }

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            string? text = null;
            if (arguments.Keys.Any(key => key != ToolExecutionArgumentNames.RequestId && key != ToolExecutionArgumentNames.SessionId && (Id != ClipboardToolIds.SetText || key != "text")))
                throw new ClipboardException("clipboard_invalid_arguments", "Unexpected clipboard argument.");
            if (Id == ClipboardToolIds.SetText)
            {
                if (!arguments.TryGetValue("text", out text) || text is null)
                    throw new ClipboardException("clipboard_invalid_arguments", "The text argument is required (an empty string is allowed).");
                ValidateText(text);
            }
            var result = backend is not null
                ? ExecuteCore(backend, text)
                : await ClipboardPlatform.RunAsync(() => ExecuteCore(ClipboardPlatform.CreateBackend(), text)).ConfigureAwait(false);
            return ToolResult.Success(result);
        }
        catch (ClipboardException error) { return ToolResult.Failure(error.Message, errorCode: error.Code); }
        catch (PlatformNotSupportedException) { return ToolResult.Failure("Clipboard is unsupported on this platform.", errorCode: "clipboard_platform_unsupported"); }
        catch (Exception) { return ToolResult.Failure("The clipboard operation failed.", errorCode: "clipboard_operation_failed"); }
    }

    private JsonObject ExecuteCore(IClipboardBackend target, string? text)
    {
        switch (Id)
        {
            case ClipboardToolIds.GetText:
                var value = target.GetText();
                if (value is not null) ValidateText(value);
                return new JsonObject { ["text"] = value, ["hasText"] = value is not null };
            case ClipboardToolIds.HasText:
                return new JsonObject { ["hasText"] = target.HasText() };
            case ClipboardToolIds.SetText:
                target.SetText(text!);
                return new JsonObject { ["updated"] = true };
            case ClipboardToolIds.Clear:
                target.Clear();
                return new JsonObject { ["cleared"] = true };
            default: throw new InvalidOperationException();
        }
    }

    private static void ValidateText(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumTextBytes)
            throw new ClipboardException("clipboard_text_too_large", "Clipboard text exceeds 65536 UTF-8 bytes.");
    }
}
