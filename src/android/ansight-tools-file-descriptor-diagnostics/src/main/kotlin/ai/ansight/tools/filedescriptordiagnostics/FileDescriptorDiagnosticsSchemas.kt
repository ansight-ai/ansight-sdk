package ai.ansight.tools.filedescriptordiagnostics

import ai.ansight.runtime.ToolSchema

internal object FileDescriptorDiagnosticsSchemas {
    private val kindValues = FileDescriptorKind.values().map { it.wireName }
    private val descriptor = ToolSchema.obj(
        description = "Open file descriptor metadata.",
        properties = mapOf(
            "descriptor" to ToolSchema.integer("File descriptor number."),
            "kind" to ToolSchema.string("Descriptor resource kind.", enumValues = kindValues),
            "target" to ToolSchema.string("Resolved descriptor target when enabled and available.", nullable = true),
            "accessMode" to ToolSchema.string(
                "Descriptor access mode when available.",
                enumValues = listOf("read_only", "write_only", "read_write", "unknown"),
                nullable = true,
            ),
            "closeOnExec" to ToolSchema.bool("Whether close-on-exec is enabled.", nullable = true),
            "descriptorFlags" to ToolSchema.integer("Raw descriptor flags when available.", nullable = true),
            "statusFlags" to ToolSchema.integer("Raw open status flags when available.", nullable = true),
            "positionBytes" to ToolSchema.integer("Current descriptor position when seekable.", nullable = true),
            "inode" to ToolSchema.integer("Backing inode when available.", nullable = true),
        ),
        required = listOf(
            "descriptor", "kind", "target", "accessMode", "closeOnExec",
            "descriptorFlags", "statusFlags", "positionBytes", "inode",
        ),
    )
    private val snapshotProperties = mapOf(
        "scanComplete" to ToolSchema.bool("Whether the collector completed its descriptor enumeration."),
        "scannedDescriptorLimit" to ToolSchema.integer("Exclusive upper descriptor bound observed by the collector."),
        "capturedAtUtc" to ToolSchema.string("UTC timestamp for capture.", format = "date-time"),
    )
    private val snapshotRequired = listOf("scanComplete", "scannedDescriptorLimit", "capturedAtUtc")

    val listOpenArguments = ToolSchema.obj(
        description = "Arguments for listing open file descriptors.",
        properties = mapOf(
            "kind" to ToolSchema.string("Optional descriptor kind filter.", enumValues = kindValues, nullable = true),
            "targetContains" to ToolSchema.string("Optional case-insensitive target substring filter.", nullable = true),
            "maxEntries" to ToolSchema.integer("Maximum descriptors to return after filtering."),
        ),
    )
    val listOpenResult = ToolSchema.obj(
        description = "Open file descriptor listing.",
        properties = snapshotProperties + mapOf(
            "count" to ToolSchema.integer("Total number of open descriptors found by the scan."),
            "matchedCount" to ToolSchema.integer("Number of descriptors matching the filters."),
            "returnedCount" to ToolSchema.integer("Number of descriptor records returned."),
            "descriptors" to ToolSchema.array(descriptor, "Open descriptor records."),
            "truncated" to ToolSchema.bool("Whether matching records were omitted by maxEntries."),
        ),
        required = snapshotRequired + listOf("count", "matchedCount", "returnedCount", "descriptors", "truncated"),
    )
    val countOpenArguments = ToolSchema.obj(
        description = "No arguments are required for counting open file descriptors.",
    )
    val countOpenResult = ToolSchema.obj(
        description = "Open file descriptor count.",
        properties = mapOf("count" to ToolSchema.integer("Number of open descriptors found by the scan.")),
        required = listOf("count"),
    )
    val inspectArguments = ToolSchema.obj(
        description = "Arguments for inspecting one open file descriptor.",
        properties = mapOf("descriptor" to ToolSchema.integer("Non-negative file descriptor number.")),
        required = listOf("descriptor"),
    )
    val inspectResult = ToolSchema.obj(
        description = "One open file descriptor record.",
        properties = mapOf(
            "descriptor" to descriptor,
            "capturedAtUtc" to ToolSchema.string("UTC timestamp for capture.", format = "date-time"),
        ),
        required = listOf("descriptor", "capturedAtUtc"),
    )
    val getUsageArguments = ToolSchema.obj(
        description = "No arguments are required for reading file descriptor usage.",
    )
    val getUsageResult = ToolSchema.obj(
        description = "File descriptor limits and current utilization.",
        properties = snapshotProperties + mapOf(
            "openCount" to ToolSchema.integer("Number of open descriptors found by the scan."),
            "softLimit" to ToolSchema.integer("Current process soft descriptor limit.", nullable = true),
            "hardLimit" to ToolSchema.integer("Current process hard descriptor limit, or null when unlimited.", nullable = true),
            "hardLimitUnlimited" to ToolSchema.bool("Whether the process hard limit is unlimited."),
            "availableBeforeSoftLimit" to ToolSchema.integer("Remaining descriptors before the soft limit.", nullable = true),
            "utilizationPercent" to ToolSchema.number("Percentage of the soft limit currently in use.", nullable = true),
        ),
        required = snapshotRequired + listOf(
            "openCount", "softLimit", "hardLimit", "hardLimitUnlimited",
            "availableBeforeSoftLimit", "utilizationPercent",
        ),
    )
}
