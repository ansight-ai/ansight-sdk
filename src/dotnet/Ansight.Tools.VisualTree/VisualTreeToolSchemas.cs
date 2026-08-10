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

    private static readonly ToolSchema OverlayMetadataSchema = ToolSchema.Object(
        description: "Small caller-provided scalar metadata dictionary that explains why the overlay exists.",
        additionalProperties: true,
        nullable: true);

    private static readonly ToolSchema OverlayRectSchema = ToolSchema.Object(
        description: "Window-relative overlay rectangle.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["x"] = ToolSchema.Number("Horizontal origin."),
            ["y"] = ToolSchema.Number("Vertical origin."),
            ["width"] = ToolSchema.Number("Rectangle width."),
            ["height"] = ToolSchema.Number("Rectangle height."),
            ["label"] = ToolSchema.String("Optional label stored with the rectangle.", nullable: true)
        },
        required: new[] { "x", "y", "width", "height" });

    private static readonly ToolSchema OverlayStyleSchema = ToolSchema.Object(
        description: "Diagnostic overlay style.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["strokeColor"] = ToolSchema.String("Normalized stroke color as #AARRGGBB."),
            ["fillColor"] = ToolSchema.String("Normalized fill color as #AARRGGBB, or null when no fill is drawn.", nullable: true),
            ["strokeWidth"] = ToolSchema.Number("Stroke width."),
            ["cornerRadius"] = ToolSchema.Number("Rectangle corner radius.")
        },
        required: new[] { "strokeColor", "strokeWidth", "cornerRadius" });

    private static readonly ToolSchema VisualPresentationSchema = ToolSchema.Object(
        description: "Small platform-neutral presentation snapshot used to approximate the rendered node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["foreground"] = ToolSchema.String("Resolved foreground color as #AARRGGBB, when available.", nullable: true),
            ["background"] = ToolSchema.String("Resolved solid background color as #AARRGGBB, when available.", nullable: true),
            ["opacity"] = ToolSchema.Number("Node-local opacity from zero through one."),
            ["text"] = ToolSchema.String("Bounded displayed text or placeholder, when available.", nullable: true),
            ["value"] = ToolSchema.String("Bounded display value. Secure text values are omitted.", nullable: true)
        },
        required: new[] { "opacity" });

    private static readonly ToolSchema OverlaySchema = ToolSchema.Object(
        description: "Live diagnostic overlay.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Overlay identifier."),
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["createdAtUtc"] = ToolSchema.String("UTC timestamp when the overlay was created.", format: "date-time"),
            ["expiresAtUtc"] = ToolSchema.String("UTC timestamp when the overlay should auto-remove, or null for persistent overlays.", format: "date-time", nullable: true),
            ["durationMs"] = ToolSchema.Integer("Requested overlay duration in milliseconds. Zero means persistent until removed."),
            ["remainingMs"] = ToolSchema.Integer("Approximate milliseconds before automatic removal, or null for persistent overlays.", nullable: true),
            ["transient"] = ToolSchema.Boolean("Whether the overlay automatically removes itself."),
            ["inputTransparent"] = ToolSchema.Boolean("Always true. The overlay is rendered with native input-transparent primitives."),
            ["coordinateSpace"] = ToolSchema.String("Coordinate space used by returned rectangles."),
            ["style"] = OverlayStyleSchema,
            ["rects"] = ToolSchema.Array(OverlayRectSchema, "Rendered rectangles."),
            ["metadata"] = OverlayMetadataSchema
        },
        required: new[] { "id", "platform", "createdAtUtc", "durationMs", "transient", "inputTransparent", "coordinateSpace", "style", "rects" });

    private static readonly ToolSchema VisualNodeSchema = ToolSchema.Object(
        description: "A visual tree node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Stable identifier for the node."),
            ["type"] = ToolSchema.String("Platform view type."),
            ["automationId"] = ToolSchema.String("Platform automation or test identifier, when present.", nullable: true),
            ["label"] = ToolSchema.String("Best-effort visible or accessibility label.", nullable: true),
            ["visible"] = ToolSchema.Boolean("Whether the node is visible."),
            ["enabled"] = ToolSchema.Boolean("Whether the node is enabled."),
            ["focusable"] = ToolSchema.Boolean("Whether the node can receive focus."),
            ["childCount"] = ToolSchema.Integer("Number of direct children."),
            ["visual"] = VisualPresentationSchema,
            ["bounds"] = BoundsSchema,
            ["properties"] = ToolSchema.Object(
                description: "Additional implementation-specific node properties.",
                additionalProperties: true,
                nullable: true),
            ["children"] = ToolSchema.Array(GenericObjectSchema, "Nested child nodes.", nullable: true)
        },
        required: new[] { "id", "type", "visible", "enabled", "focusable", "childCount", "visual" });

    internal static ToolSchema GetVisualTreeArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving the current visual tree.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["source"] = ToolSchema.String("Optional visual tree provider source. Defaults to native.", nullable: true),
            ["includeBounds"] = ToolSchema.Boolean("Include node bounds in the result."),
            ["includeComputedStyles"] = ToolSchema.Boolean("Include implementation-specific node properties."),
            ["maxDepth"] = ToolSchema.Integer("Maximum child depth to include."),
            ["maxNodes"] = ToolSchema.Integer("Maximum number of nodes to capture."),
            ["rootNodeId"] = ToolSchema.String("Optional node id to use as the subtree root.", nullable: true)
        });

    internal static ToolSchema VisualTreeResult { get; } = ToolSchema.Object(
        description: "Visual tree payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["format"] = ToolSchema.String("Versioned visual-tree payload format.", nullable: true),
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["source"] = ToolSchema.String("Visual-tree provider source.", nullable: true),
            ["adapter"] = ToolSchema.String("Provider adapter identifier.", nullable: true),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["root"] = VisualNodeSchema
        },
        required: new[] { "platform", "capturedAtUtc", "root" });

    internal static ToolSchema InspectNodeArguments { get; } = ToolSchema.Object(
        description: "Arguments for inspecting a specific node.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["source"] = ToolSchema.String("Optional visual tree provider source. Defaults to native.", nullable: true),
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
            ["source"] = ToolSchema.String("Visual-tree provider source.", nullable: true),
            ["adapter"] = ToolSchema.String("Provider adapter identifier.", nullable: true),
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

    internal static ToolSchema ShowOverlayArguments { get; } = ToolSchema.Object(
        description: "Arguments for drawing an input-transparent diagnostic overlay over the active app window.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["overlayId"] = ToolSchema.String("Optional overlay id. When omitted, a generated id is returned.", nullable: true),
            ["nodeId"] = ToolSchema.String("Optional visual tree node id to highlight. Pass either nodeId or rectangle coordinates.", nullable: true),
            ["rects"] = ToolSchema.Array(OverlayRectSchema, "Optional rectangles to draw. Pass either rects or top-level x/y/width/height.", nullable: true),
            ["x"] = ToolSchema.Number("Single rectangle horizontal origin.", nullable: true),
            ["y"] = ToolSchema.Number("Single rectangle vertical origin.", nullable: true),
            ["width"] = ToolSchema.Number("Single rectangle width.", nullable: true),
            ["height"] = ToolSchema.Number("Single rectangle height.", nullable: true),
            ["label"] = ToolSchema.String("Optional label for a single rectangle.", nullable: true),
            ["coordinateSpace"] = ToolSchema.String(
                "Input coordinate space. Defaults to window. visualTree accepts bounds returned by ui.get_visual_tree.",
                enumValues: new[] { "visualTree", "window" }),
            ["strokeColor"] = ToolSchema.String("Stroke color. Supports #RGB, #ARGB, #RRGGBB, #AARRGGBB, or common color names."),
            ["fillColor"] = ToolSchema.String("Fill color. Omit, null, none, or transparent for no fill.", nullable: true),
            ["strokeWidth"] = ToolSchema.Number("Stroke width."),
            ["cornerRadius"] = ToolSchema.Number("Rectangle corner radius."),
            ["durationMs"] = ToolSchema.Integer("Overlay lifetime in milliseconds. Defaults to 5000. Pass 0 to persist until removed."),
            ["metadata"] = OverlayMetadataSchema
        });

    internal static ToolSchema OverlayResult { get; } = ToolSchema.Object(
        description: "Single overlay payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["overlay"] = OverlaySchema
        },
        required: new[] { "platform", "capturedAtUtc", "overlay" });

    internal static ToolSchema GetOverlayArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving a diagnostic overlay by id.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["overlayId"] = ToolSchema.String("Overlay id.")
        },
        required: new[] { "overlayId" });

    internal static ToolSchema QueryOverlaysArguments { get; } = ToolSchema.Object(
        description: "Arguments for querying live diagnostic overlays.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["metadataKey"] = ToolSchema.String("Optional metadata key that must be present.", nullable: true),
            ["metadataValue"] = ToolSchema.String("Optional metadata value that must match the metadataKey value.", nullable: true)
        });

    internal static ToolSchema QueryOverlaysResult { get; } = ToolSchema.Object(
        description: "Overlay query payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time"),
            ["count"] = ToolSchema.Integer("Number of matching overlays."),
            ["overlays"] = ToolSchema.Array(OverlaySchema, "Matching overlays.")
        },
        required: new[] { "platform", "capturedAtUtc", "count", "overlays" });

    internal static ToolSchema UpdateOverlayArguments { get; } = ToolSchema.Object(
        description: "Arguments for editing an existing diagnostic overlay. Omitted fields preserve the current overlay value.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["overlayId"] = ToolSchema.String("Overlay id."),
            ["nodeId"] = ToolSchema.String("Optional visual tree node id to highlight. Replaces existing rectangles.", nullable: true),
            ["rects"] = ToolSchema.Array(OverlayRectSchema, "Optional replacement rectangles to draw.", nullable: true),
            ["x"] = ToolSchema.Number("Single replacement rectangle horizontal origin.", nullable: true),
            ["y"] = ToolSchema.Number("Single replacement rectangle vertical origin.", nullable: true),
            ["width"] = ToolSchema.Number("Single replacement rectangle width.", nullable: true),
            ["height"] = ToolSchema.Number("Single replacement rectangle height.", nullable: true),
            ["label"] = ToolSchema.String("Optional label for a single replacement rectangle.", nullable: true),
            ["coordinateSpace"] = ToolSchema.String(
                "Input coordinate space for replacement geometry. Defaults to window. visualTree accepts bounds returned by ui.get_visual_tree.",
                enumValues: new[] { "visualTree", "window" }),
            ["strokeColor"] = ToolSchema.String("Replacement stroke color. Supports #RGB, #ARGB, #RRGGBB, #AARRGGBB, or common color names.", nullable: true),
            ["fillColor"] = ToolSchema.String("Replacement fill color. Use none or transparent to clear fill.", nullable: true),
            ["strokeWidth"] = ToolSchema.Number("Replacement stroke width.", nullable: true),
            ["cornerRadius"] = ToolSchema.Number("Replacement rectangle corner radius.", nullable: true),
            ["durationMs"] = ToolSchema.Integer("Replacement overlay lifetime from now in milliseconds. Pass 0 to persist until removed.", nullable: true),
            ["metadata"] = OverlayMetadataSchema,
            ["metadataMode"] = ToolSchema.String(
                "How provided metadata should be applied. merge preserves unspecified keys; replace swaps the dictionary; clear removes all metadata.",
                enumValues: new[] { "merge", "replace", "clear" })
        },
        required: new[] { "overlayId" });

    internal static ToolSchema RemoveOverlayArguments { get; } = ToolSchema.Object(
        description: "Arguments for removing a diagnostic overlay by id.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["overlayId"] = ToolSchema.String("Overlay id.")
        },
        required: new[] { "overlayId" });

    internal static ToolSchema RemoveOverlayResult { get; } = ToolSchema.Object(
        description: "Overlay removal payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for removal.", format: "date-time"),
            ["overlayId"] = ToolSchema.String("Overlay id."),
            ["removed"] = ToolSchema.Boolean("Whether a live overlay was found and removed."),
            ["overlay"] = ToolSchema.Object(
                description: "Removed overlay snapshot, or null when no overlay matched.",
                additionalProperties: true,
                nullable: true)
        },
        required: new[] { "platform", "capturedAtUtc", "overlayId", "removed" });

    internal static ToolSchema ClearOverlaysArguments { get; } = ToolSchema.Object(
        description: "Arguments for clearing diagnostic overlays.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["metadataKey"] = ToolSchema.String("Optional metadata key that must be present for an overlay to be cleared.", nullable: true),
            ["metadataValue"] = ToolSchema.String("Optional metadata value that must match the metadataKey value for an overlay to be cleared.", nullable: true)
        });

    internal static ToolSchema ClearOverlaysResult { get; } = ToolSchema.Object(
        description: "Overlay clear payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["platform"] = ToolSchema.String("Current runtime platform."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for removal.", format: "date-time"),
            ["count"] = ToolSchema.Integer("Number of overlays removed."),
            ["overlays"] = ToolSchema.Array(OverlaySchema, "Removed overlay snapshots.")
        },
        required: new[] { "platform", "capturedAtUtc", "count", "overlays" });
}
