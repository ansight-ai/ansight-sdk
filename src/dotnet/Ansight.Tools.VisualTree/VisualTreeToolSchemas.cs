namespace Ansight.Tools.VisualTree;

using Ansight.Tools;

internal static class VisualTreeToolSchemas
{
    private static readonly ToolSchema BoundsSchema = ToolSchema.Object(
        description: "Screen-space bounds for a visual node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["x"] = ToolSchema.Number("Horizontal origin."),
            ["y"] = ToolSchema.Number("Vertical origin."),
            ["width"] = ToolSchema.Number("Width of the node."),
            ["height"] = ToolSchema.Number("Height of the node.")
        },
        required: new[] { "x", "y", "width", "height" });

    private static readonly ToolSchema GenericObjectSchema = ToolSchema.Object(
        description: "Arbitrary object with implementation-specific fields.",
        additionalProperties: true);

    private static readonly ToolSchema VisualNodeSchema = ToolSchema.Object(
        description: "A visual tree node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Stable identifier for the node."),
            ["type"] = ToolSchema.String("Platform view type."),
            ["label"] = ToolSchema.String("Best-effort visible or accessibility label.", nullable: true),
            ["visible"] = ToolSchema.Boolean("Whether the node is visible."),
            ["enabled"] = ToolSchema.Boolean("Whether the node is enabled."),
            ["focusable"] = ToolSchema.Boolean("Whether the node can receive focus."),
            ["childCount"] = ToolSchema.Integer("Number of direct children."),
            ["bounds"] = BoundsSchema,
            ["properties"] = ToolSchema.Object(
                description: "Additional implementation-specific node properties.",
                additionalProperties: true,
                nullable: true),
            ["children"] = ToolSchema.Array(GenericObjectSchema, "Nested child nodes.", nullable: true)
        },
        required: new[] { "id", "type", "visible", "enabled", "focusable", "childCount" });

    internal static ToolSchema GetVisualTreeArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving the current visual tree.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["includeBounds"] = ToolSchema.Boolean("Include node bounds in the result."),
            ["includeComputedStyles"] = ToolSchema.Boolean("Include implementation-specific node properties."),
            ["maxDepth"] = ToolSchema.Integer("Maximum child depth to include."),
            ["rootNodeId"] = ToolSchema.String("Optional node id to use as the subtree root.", nullable: true)
        });

    internal static ToolSchema VisualTreeResult { get; } = ToolSchema.Object(
        description: "Visual tree payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["root"] = VisualNodeSchema
        },
        required: new[] { "platform", "capturedAtUtc", "root" });

    internal static ToolSchema InspectNodeArguments { get; } = ToolSchema.Object(
        description: "Arguments for inspecting a specific node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["nodeId"] = ToolSchema.String("Identifier of the node to inspect."),
            ["includeAncestors"] = ToolSchema.Boolean("Include ancestor nodes in the response."),
            ["includeDescendants"] = ToolSchema.Boolean("Include descendant nodes in the response."),
            ["includeProperties"] = ToolSchema.Boolean("Include implementation-specific node properties.")
        },
        required: new[] { "nodeId" });

    internal static ToolSchema InspectNodeResult { get; } = ToolSchema.Object(
        description: "Detailed node inspection payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["node"] = VisualNodeSchema,
            ["ancestors"] = ToolSchema.Array(VisualNodeSchema, "Optional ancestor chain.", nullable: true),
            ["descendants"] = ToolSchema.Array(GenericObjectSchema, "Optional descendant list.", nullable: true)
        },
        required: new[] { "platform", "capturedAtUtc", "node" });

    internal static ToolSchema GetScreenshotArguments { get; } = ToolSchema.Object(
        description: "Arguments for capturing a screenshot.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["format"] = ToolSchema.String("Image format.", enumValues: new[] { "png", "jpeg" }),
            ["quality"] = ToolSchema.Integer("Compression quality for JPEG output."),
            ["maxWidth"] = ToolSchema.Integer("Optional maximum output width.", nullable: true),
            ["annotateNodeIds"] = ToolSchema.Boolean("Whether node ids should be drawn over the screenshot."),
            ["afterScreenUpdates"] = ToolSchema.Boolean("Whether Apple platforms should wait for pending screen updates before rendering.")
        });

    internal static ToolSchema ScreenshotResult { get; } = ToolSchema.Object(
        description: "Screenshot payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["format"] = ToolSchema.String("Encoded image format.", enumValues: new[] { "png", "jpeg" }),
            ["width"] = ToolSchema.Integer("Rendered image width in pixels."),
            ["height"] = ToolSchema.Integer("Rendered image height in pixels."),
            ["deliveryMode"] = ToolSchema.String("Transport used for the image bytes.", enumValues: new[] { "websocket_binary" }, nullable: true),
            ["wireProtocol"] = ToolSchema.String("Binary transfer wire protocol name.", nullable: true),
            ["transferId"] = ToolSchema.String("Binary transfer identifier.", nullable: true),
            ["downloadId"] = ToolSchema.String("Tool request id that owns the queued binary transfer.", nullable: true),
            ["sizeBytes"] = ToolSchema.Integer("Expected binary image byte count.", nullable: true),
            ["fileName"] = ToolSchema.String("Suggested screenshot artifact file name.", nullable: true),
            ["mimeType"] = ToolSchema.String("Screenshot MIME type.", nullable: true),
            ["artifactPath"] = ToolSchema.String("Host-side artifact path once the binary transfer completes.", nullable: true),
            ["artifactKind"] = ToolSchema.String("Host-side artifact kind.", enumValues: new[] { "screenshot" }, nullable: true),
            ["status"] = ToolSchema.String("Binary transfer status.", enumValues: new[] { "queued", "receiving", "complete", "failed" }, nullable: true),
            ["receivedBytes"] = ToolSchema.Integer("Binary image bytes received by the host.", nullable: true),
            ["annotationApplied"] = ToolSchema.Boolean("Whether node id annotations were applied.")
        },
        required: new[] { "platform", "capturedAtUtc", "format", "width", "height", "annotationApplied" });
}
