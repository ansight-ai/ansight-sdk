import AnsightCore
import Foundation

internal enum AnsightVisualTreeToolSchemas {
    static let getVisualTreeArguments = object(
        description: "Arguments for retrieving the current visual tree.",
        properties: [
            "source": string("Optional visual tree provider source. Defaults to native.", nullable: true),
            "includeBounds": boolean("Include node bounds in the result."),
            "includeComputedStyles": boolean("Include implementation-specific node properties."),
            "maxDepth": integer("Maximum child depth to include."),
            "rootNodeId": string("Optional node id to use as the subtree root.", nullable: true),
        ]
    )

    static let visualTreeResult = object(
        description: "Visual tree payload.",
        properties: [
            "format": string("Versioned compact visual-tree format."),
            "platform": string("Current runtime platform."),
            "source": string("Visual tree provider source."),
            "adapter": string("Implementation adapter that produced the tree.", nullable: true),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "types": array(string("Registered platform view type."), "Type registry referenced by node typeId fields."),
            "root": visualNode,
        ],
        required: ["format", "platform", "source", "capturedAtUtc", "types", "root"]
    )

    static let inspectNodeArguments = object(
        description: "Arguments for inspecting a specific node.",
        properties: [
            "reference": nodeReference,
            "source": string("Optional visual tree provider source. Defaults to native.", nullable: true),
            "snapshotId": string("Optional current snapshot that owns nodeId. May be supplied through reference.", nullable: true),
            "nodeId": string("Identifier of the node to inspect. May be supplied through reference.", nullable: true),
            "includeAncestors": boolean("Include ancestor nodes in the response."),
            "includeDescendants": boolean("Include descendant nodes in the response."),
            "includeProperties": boolean("Include implementation-specific node properties."),
        ]
    )

    static let inspectNodeResult = object(
        description: "Detailed node inspection payload.",
        properties: [
            "format": string("Versioned compact visual-tree format."),
            "platform": string("Current runtime platform."),
            "source": string("Visual tree provider source."),
            "adapter": string("Implementation adapter that produced the tree.", nullable: true),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "snapshotId": string("Snapshot that owns the returned node reference."),
            "revision": integer("Monotonic process-local snapshot revision."),
            "reference": nodeReference,
            "types": array(string("Registered platform view type."), "Type registry referenced by node typeId fields."),
            "node": visualNode,
            "ancestors": array(visualNode, "Optional ancestor chain.", nullable: true),
            "descendants": array(genericObject, "Optional descendant list.", nullable: true),
        ],
        required: ["format", "platform", "source", "capturedAtUtc", "snapshotId", "revision", "reference", "types", "node"]
    )

    static let queryNodesArguments = object(
        description: "Framework-neutral UI node query and capture options.",
        properties: queryFilterProperties
    )

    static let queryNodesResult = object(
        description: "Snapshot-scoped UI query result.",
        properties: [
            "source": string("Visual-tree provider source."),
            "snapshotId": string("Snapshot searched by the query."),
            "revision": integer("Monotonic process-local snapshot revision."),
            "count": integer("Number of returned matches."),
            "totalMatches": integer("Total matches before result limiting."),
            "truncated": boolean("Whether matching results were limited."),
            "matches": array(objectJSON(
                description: "Provider-neutral node data with a snapshot-scoped reference.",
                properties: ["reference": nodeReference],
                required: ["reference"],
                additionalProperties: true
            ), "Matching nodes."),
        ],
        required: ["source", "snapshotId", "revision", "count", "totalMatches", "truncated", "matches"]
    )

    static let performActionArguments = object(
        description: "Framework-neutral action targeting a snapshot-scoped node.",
        properties: [
            "reference": nodeReference,
            "source": string("Optional visual-tree provider source.", nullable: true),
            "snapshotId": string("Current snapshot that owns nodeId. May be supplied through reference.", nullable: true),
            "nodeId": string("Node id within snapshotId. May be supplied through reference.", nullable: true),
            "action": string("Semantic action.", enumValues: ["tap", "focus", "unfocus", "setValue", "typeText", "toggle", "select", "selectTab"]),
            "value": string("String action value.", nullable: true),
            "index": integer("Numeric selection index.", nullable: true),
            "checked": boolean("Explicit boolean value.", nullable: true),
            "options": objectJSON(description: "Provider-specific action options.", additionalProperties: true, nullable: true),
        ],
        required: ["action"]
    )

    static let performActionResult = AnsightToolSchema(json: objectJSON(
        description: "Framework-neutral UI action result.",
        properties: [
            "source": string("Visual-tree provider source."),
            "action": string("Performed semantic action."),
            "reference": nodeReference,
        ],
        required: ["source", "action", "reference"],
        additionalProperties: true
    ))

    static let waitArguments = object(
        description: "Framework-neutral UI wait condition and query filters.",
        properties: queryFilterProperties.merging([
            "condition": string("Condition to wait for.", enumValues: ["exists", "notExists", "visible", "enabled"]),
            "timeoutMilliseconds": integer("Maximum wait duration."),
            "pollMilliseconds": integer("Delay between fresh snapshots."),
        ]) { _, new in new },
        required: ["condition"]
    )

    static let waitResult = object(
        description: "Successful UI wait result.",
        properties: [
            "condition": string("Satisfied condition."),
            "matched": boolean("Whether the condition matched."),
            "elapsedMilliseconds": integer("Elapsed wait duration."),
            "query": queryNodesResult.json,
        ],
        required: ["condition", "matched", "elapsedMilliseconds", "query"]
    )

    static let getScreenshotArguments = object(
        description: "Arguments for capturing a screenshot.",
        properties: [
            "format": string("Image format.", enumValues: ["png", "jpeg"]),
            "quality": integer("Compression quality for JPEG output."),
            "maxWidth": integer("Optional maximum output width.", nullable: true),
            "annotateNodeIds": boolean("Whether node ids should be drawn over the screenshot."),
            "afterScreenUpdates": boolean("Whether Apple platforms should wait for pending screen updates before rendering."),
        ]
    )

    static let screenshotResult = object(
        description: "Screenshot payload.",
        properties: [
            "platform": string("Current runtime platform."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "format": string("Encoded image format.", enumValues: ["png", "jpeg"]),
            "width": integer("Rendered image width in pixels."),
            "height": integer("Rendered image height in pixels."),
            "deliveryMode": string("Transport used for the image bytes.", enumValues: ["websocket_binary"], nullable: true),
            "wireProtocol": string("Binary transfer wire protocol name.", nullable: true),
            "transferId": string("Binary transfer identifier.", nullable: true),
            "downloadId": string("Tool request id that owns the queued binary transfer.", nullable: true),
            "sizeBytes": integer("Expected binary image byte count.", nullable: true),
            "fileName": string("Suggested screenshot artifact file name.", nullable: true),
            "mimeType": string("Screenshot MIME type.", nullable: true),
            "artifactPath": string("Host-side artifact path once the binary transfer completes.", nullable: true),
            "artifactKind": string("Host-side artifact kind.", enumValues: ["screenshot"], nullable: true),
            "status": string("Binary transfer status.", enumValues: ["queued", "receiving", "complete", "failed"], nullable: true),
            "receivedBytes": integer("Binary image bytes received by the host.", nullable: true),
            "annotationApplied": boolean("Whether node id annotations were applied."),
        ],
        required: ["platform", "capturedAtUtc", "format", "width", "height", "annotationApplied"]
    )

    static let showOverlayArguments = object(
        description: "Arguments for drawing an input-transparent diagnostic overlay over the active app window.",
        properties: overlayMutationProperties(requiredOverlayId: false)
    )

    static let overlayResult = object(
        description: "Single overlay payload.",
        properties: [
            "platform": string("Current runtime platform."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "overlay": overlay,
        ],
        required: ["platform", "capturedAtUtc", "overlay"]
    )

    static let getOverlayArguments = object(
        description: "Arguments for retrieving a diagnostic overlay by id.",
        properties: [
            "overlayId": string("Overlay id."),
        ],
        required: ["overlayId"]
    )

    static let queryOverlaysArguments = object(
        description: "Arguments for querying live diagnostic overlays.",
        properties: [
            "metadataKey": string("Optional metadata key that must be present.", nullable: true),
            "metadataValue": string("Optional metadata value that must match the metadataKey value.", nullable: true),
        ]
    )

    static let queryOverlaysResult = object(
        description: "Overlay query payload.",
        properties: [
            "platform": string("Current runtime platform."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
            "count": integer("Number of matching overlays."),
            "overlays": array(overlay, "Matching overlays."),
        ],
        required: ["platform", "capturedAtUtc", "count", "overlays"]
    )

    static let updateOverlayArguments = object(
        description: "Arguments for editing an existing diagnostic overlay. Omitted fields preserve the current overlay value.",
        properties: overlayMutationProperties(requiredOverlayId: true).merging([
            "metadataMode": string(
                "How provided metadata should be applied.",
                enumValues: ["merge", "replace", "clear"]
            ),
        ]) { _, new in new },
        required: ["overlayId"]
    )

    static let removeOverlayArguments = object(
        description: "Arguments for removing a diagnostic overlay by id.",
        properties: [
            "overlayId": string("Overlay id."),
        ],
        required: ["overlayId"]
    )

    static let removeOverlayResult = object(
        description: "Overlay removal payload.",
        properties: [
            "platform": string("Current runtime platform."),
            "capturedAtUtc": string("UTC timestamp for removal.", format: "date-time"),
            "overlayId": string("Overlay id."),
            "removed": boolean("Whether a live overlay was found and removed."),
            "overlay": genericObjectNullable,
        ],
        required: ["platform", "capturedAtUtc", "overlayId", "removed"]
    )

    static let clearOverlaysArguments = object(
        description: "Arguments for clearing diagnostic overlays.",
        properties: [
            "metadataKey": string("Optional metadata key that must be present for an overlay to be cleared.", nullable: true),
            "metadataValue": string("Optional metadata value that must match the metadataKey value for an overlay to be cleared.", nullable: true),
        ]
    )

    static let clearOverlaysResult = object(
        description: "Overlay clear payload.",
        properties: [
            "platform": string("Current runtime platform."),
            "capturedAtUtc": string("UTC timestamp for removal.", format: "date-time"),
            "count": integer("Number of overlays removed."),
            "overlays": array(overlay, "Removed overlay snapshots."),
        ],
        required: ["platform", "capturedAtUtc", "count", "overlays"]
    )

    private static let bounds = objectJSON(
        description: "Screen-space bounds for a visual node.",
        properties: [
            "x": number("Horizontal origin."),
            "y": number("Vertical origin."),
            "width": number("Width of the node."),
            "height": number("Height of the node."),
        ],
        required: ["x", "y", "width", "height"]
    )

    private static let genericObject = objectJSON(
        description: "Arbitrary object with implementation-specific fields.",
        additionalProperties: true
    )

    private static let nodeReference = objectJSON(
        description: "Snapshot-scoped UI node identity.",
        properties: [
            "source": string("Visual-tree provider source."),
            "snapshotId": string("Snapshot that owns the node identity."),
            "revision": integer("Monotonic process-local snapshot revision."),
            "nodeId": string("Provider node id, valid only within the snapshot."),
        ],
        required: ["source", "snapshotId", "revision", "nodeId"]
    )

    private static let queryFilterProperties: [String: JSONValue] = [
        "source": string("Optional visual-tree provider source.", nullable: true),
        "snapshotId": string("Optional current snapshot to query.", nullable: true),
        "nodeId": string("Exact node id.", nullable: true),
        "automationId": string("Exact platform automation id.", nullable: true),
        "role": string("Exact semantic role.", nullable: true),
        "type": string("Case-insensitive type-name fragment.", nullable: true),
        "textContains": string("Case-insensitive visible/accessibility text fragment.", nullable: true),
        "action": string("Required supported semantic action.", nullable: true),
        "visible": boolean("Match visible state.", nullable: true),
        "enabled": boolean("Match enabled state.", nullable: true),
        "maxResults": integer("Maximum returned matches."),
        "includeBounds": boolean("Include bounds during fresh capture."),
        "includeComputedStyles": boolean("Include native properties during fresh capture."),
        "includeProperties": boolean("Include framework properties during fresh capture."),
        "includeInactivePages": boolean("Include inactive framework pages during fresh capture."),
        "root": string("Optional provider root scope.", nullable: true),
        "rootNodeId": string("Optional subtree root.", nullable: true),
        "maxDepth": integer("Maximum capture depth."),
        "maxNodes": integer("Maximum capture nodes."),
    ]

    private static let genericObjectNullable = objectJSON(
        description: "Arbitrary object with implementation-specific fields, or null.",
        additionalProperties: true,
        nullable: true
    )

    private static let visualNode = objectJSON(
        description: "A visual tree node.",
        properties: [
            "id": string("Stable identifier for the node."),
            "typeId": integer("Index into the payload types registry."),
            "automationId": string("Platform automation or test identifier, when present.", nullable: true),
            "label": string("Best-effort visible or accessibility label.", nullable: true),
            "role": string("Platform-neutral semantic role."),
            "supportedActions": array(string("Semantic action."), "Semantic actions supported by the node."),
            "interactable": boolean("Whether the node is visible, enabled, and exposes at least one action."),
            "visible": boolean("Whether the node is visible."),
            "enabled": boolean("Whether the node is enabled."),
            "focusable": boolean("Whether the node can receive focus."),
            "childCount": integer("Number of direct children."),
            "visual": visualPresentation,
            "z": number("Optional normalized stacking priority relative to sibling nodes. Child order records default sibling order; the screenshot remains authoritative for final compositing.", nullable: true),
            "bounds": bounds,
            "properties": objectJSON(
                description: "Additional implementation-specific node properties.",
                additionalProperties: true,
                nullable: true
            ),
            "children": array(genericObject, "Nested child nodes.", nullable: true),
        ],
        required: ["id", "typeId", "role", "supportedActions", "interactable", "visible", "enabled", "focusable", "childCount", "visual"]
    )

    private static let visualPresentation = objectJSON(
        description: "Small platform-neutral presentation snapshot used to approximate the rendered node.",
        properties: [
            "foreground": string("Resolved foreground color as #AARRGGBB, when available.", nullable: true),
            "background": string("Resolved solid background color as #AARRGGBB, when available.", nullable: true),
            "opacity": number("Node-local opacity from zero through one."),
            "text": string("Bounded displayed text or placeholder, when available.", nullable: true),
            "value": string("Bounded display value. Secure text values are omitted.", nullable: true),
        ],
        required: ["opacity"]
    )

    private static let overlayRect = objectJSON(
        description: "Window-relative overlay rectangle.",
        properties: [
            "x": number("Horizontal origin."),
            "y": number("Vertical origin."),
            "width": number("Rectangle width."),
            "height": number("Rectangle height."),
            "label": string("Optional label stored with the rectangle.", nullable: true),
        ],
        required: ["x", "y", "width", "height"]
    )

    private static let overlayStyle = objectJSON(
        description: "Diagnostic overlay style.",
        properties: [
            "strokeColor": string("Normalized stroke color as #AARRGGBB."),
            "fillColor": string("Normalized fill color as #AARRGGBB, or null when no fill is drawn.", nullable: true),
            "strokeWidth": number("Stroke width."),
            "cornerRadius": number("Rectangle corner radius."),
        ],
        required: ["strokeColor", "strokeWidth", "cornerRadius"]
    )

    private static let overlay = objectJSON(
        description: "Live diagnostic overlay.",
        properties: [
            "id": string("Overlay identifier."),
            "platform": string("Current runtime platform."),
            "createdAtUtc": string("UTC timestamp when the overlay was created.", format: "date-time"),
            "expiresAtUtc": string("UTC timestamp when the overlay should auto-remove, or null for persistent overlays.", nullable: true, format: "date-time"),
            "durationMs": integer("Requested overlay duration in milliseconds. Zero means persistent until removed."),
            "remainingMs": integer("Approximate milliseconds before automatic removal, or null for persistent overlays.", nullable: true),
            "transient": boolean("Whether the overlay automatically removes itself."),
            "inputTransparent": boolean("Always true. The overlay is rendered with native input-transparent primitives."),
            "coordinateSpace": string("Coordinate space used by returned rectangles."),
            "style": overlayStyle,
            "rects": array(overlayRect, "Rendered rectangles."),
            "metadata": objectJSON(
                description: "Small caller-provided scalar metadata dictionary that explains why the overlay exists.",
                additionalProperties: true,
                nullable: true
            ),
        ],
        required: ["id", "platform", "createdAtUtc", "durationMs", "transient", "inputTransparent", "coordinateSpace", "style", "rects"]
    )

    private static func overlayMutationProperties(requiredOverlayId: Bool) -> [String: JSONValue] {
        [
            "overlayId": string(requiredOverlayId ? "Overlay id." : "Optional overlay id. When omitted, a generated id is returned.", nullable: !requiredOverlayId),
            "nodeId": string("Optional visual tree node id to highlight. Pass either nodeId or rectangle coordinates.", nullable: true),
            "rects": array(overlayRect, "Optional rectangles to draw. Pass either rects or top-level x/y/width/height.", nullable: true),
            "x": number("Single rectangle horizontal origin.", nullable: true),
            "y": number("Single rectangle vertical origin.", nullable: true),
            "width": number("Single rectangle width.", nullable: true),
            "height": number("Single rectangle height.", nullable: true),
            "label": string("Optional label for a single rectangle.", nullable: true),
            "coordinateSpace": string("Input coordinate space.", enumValues: ["visualTree", "window"]),
            "strokeColor": string("Stroke color. Supports #RGB, #ARGB, #RRGGBB, #AARRGGBB, or common color names.", nullable: requiredOverlayId),
            "fillColor": string("Fill color. Omit, null, none, or transparent for no fill.", nullable: true),
            "strokeWidth": number("Stroke width.", nullable: requiredOverlayId),
            "cornerRadius": number("Rectangle corner radius.", nullable: requiredOverlayId),
            "durationMs": integer("Overlay lifetime in milliseconds. Defaults to 5000. Pass 0 to persist until removed.", nullable: requiredOverlayId),
            "metadata": objectJSON(
                description: "Small caller-provided scalar metadata dictionary that explains why the overlay exists.",
                additionalProperties: true,
                nullable: true
            ),
        ]
    }

    private static func object(
        description: String,
        properties: [String: JSONValue],
        required: [String] = []
    ) -> AnsightToolSchema {
        AnsightToolSchema(json: objectJSON(description: description, properties: properties, required: required))
    }

    private static func objectJSON(
        description: String,
        properties: [String: JSONValue] = [:],
        required: [String] = [],
        additionalProperties: Bool = false,
        nullable: Bool = false
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string("object"), .string("null")]) : .string("object"),
            "additionalProperties": .bool(additionalProperties),
            "description": .string(description),
        ]

        if !properties.isEmpty {
            result["properties"] = .object(properties)
        }

        if !required.isEmpty {
            result["required"] = .array(required.map(JSONValue.string))
        }

        return .object(result)
    }

    private static func array(_ items: JSONValue, _ description: String, nullable: Bool = false) -> JSONValue {
        .object([
            "type": nullable ? .array([.string("array"), .string("null")]) : .string("array"),
            "additionalProperties": .bool(false),
            "description": .string(description),
            "items": items,
        ])
    }

    private static func string(
        _ description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        primitive(type: "string", description: description, enumValues: enumValues, nullable: nullable, format: format)
    }

    private static func integer(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "integer", description: description, nullable: nullable)
    }

    private static func number(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "number", description: description, nullable: nullable)
    }

    private static func boolean(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "boolean", description: description, nullable: nullable)
    }

    private static func primitive(
        type: String,
        description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string(type), .string("null")]) : .string(type),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]

        if !enumValues.isEmpty {
            result["enum"] = .array(enumValues.map(JSONValue.string))
        }

        if let format {
            result["format"] = .string(format)
        }

        return .object(result)
    }
}
