namespace Ansight.Tools;

/// <summary>Message identifiers for the app-to-host remote-tool wire protocol.</summary>
public static class ToolProtocolMessageTypes
{
    public const string Capability = "tool.exec";
    public const string QueryType = "tool.query";
    public const string CatalogType = "tool.catalog";
    public const string CallType = "tool.call";
    public const string BatchType = "tool.batch";
    public const string ResultType = "tool.result";
    public const string BatchResultType = "tool.batch.result";
    public const string ErrorType = "tool.error";
    public const string CatalogSchema = "ansight.tool-catalog.v3";
}
