namespace Ansight.Tools.Reflection;

using Ansight.Tools;

internal static class ReflectionToolSchemas
{
    private static readonly ToolSchema GenericObjectSchema = ToolSchema.Object(
        description: "Arbitrary object with implementation-specific fields.",
        additionalProperties: true);

    private static readonly ToolSchema MetadataSchema = ToolSchema.Object(
        description: "Registered root metadata.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["displayName"] = ToolSchema.String("Human-readable root name."),
            ["description"] = ToolSchema.String("Optional root description.", nullable: true),
            ["hints"] = ToolSchema.Array(ToolSchema.String("Root hint."), "Optional metadata hints.", nullable: true)
        },
        required: new[] { "displayName" });

    private static readonly ToolSchema HostRuntimeSchema = ToolSchema.Object(
        description: "Runtime that owns and resolves the reflection root.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["kind"] = ToolSchema.String("Stable runtime host kind, such as dotnet, jvm, swift, or javascript."),
            ["displayName"] = ToolSchema.String("Human-readable runtime host name."),
            ["platform"] = ToolSchema.String("Platform or SDK surface that exposes the runtime.", nullable: true),
            ["engine"] = ToolSchema.String("Runtime engine name when known.", nullable: true),
            ["bridge"] = ToolSchema.String("Optional bridge used to reach the runtime.", nullable: true)
        },
        additionalProperties: true,
        required: new[] { "kind", "displayName" });

    private static readonly ToolSchema RootDescriptorSchema = ToolSchema.Object(
        description: "Registered reflection root descriptor.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Stable root identifier."),
            ["metadata"] = MetadataSchema,
            ["hostRuntime"] = HostRuntimeSchema,
            ["referenceType"] = ToolSchema.String("Reference type for the root.", enumValues: new[] { "weak", "strong", "getter" }),
            ["available"] = ToolSchema.Boolean("Whether the root currently resolves to a live object."),
            ["runtimeType"] = ToolSchema.String("Resolved runtime type name when available.", nullable: true),
            ["memberVisibility"] = ToolSchema.String("Effective member visibility.", enumValues: new[] { "PublicOnly", "PublicAndNonPublic" }),
            ["resolutionError"] = ToolSchema.String("Safe error summary when resolution failed.", nullable: true)
        },
        required: new[] { "id", "metadata", "hostRuntime", "referenceType", "available", "memberVisibility" });

    private static readonly ToolSchema TypeMemberSchema = ToolSchema.Object(
        description: "Field or property descriptor.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Member name."),
            ["memberType"] = ToolSchema.String("Member category.", enumValues: new[] { "field", "property" }),
            ["declaringType"] = ToolSchema.String("Declaring type name."),
            ["type"] = ToolSchema.String("Member type name."),
            ["readable"] = ToolSchema.Boolean("Whether the member can be read."),
            ["writable"] = ToolSchema.Boolean("Whether the member can be written."),
            ["visibility"] = ToolSchema.String("Member visibility.", enumValues: new[] { "public", "non_public" })
        },
        required: new[] { "name", "memberType", "declaringType", "type", "readable", "writable", "visibility" });

    private static readonly ToolSchema MethodSchema = ToolSchema.Object(
        description: "Method descriptor.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Method name."),
            ["signature"] = ToolSchema.String("Canonical method signature."),
            ["declaringType"] = ToolSchema.String("Declaring type name."),
            ["returnType"] = ToolSchema.String("Method return type."),
            ["parameterTypes"] = ToolSchema.Array(ToolSchema.String("Parameter type name."), "Method parameter types."),
            ["visibility"] = ToolSchema.String("Method visibility.", enumValues: new[] { "public", "non_public" }),
            ["invokable"] = ToolSchema.Boolean("Whether the method is currently invokable through the registered root.", nullable: true)
        },
        required: new[] { "name", "signature", "declaringType", "returnType", "parameterTypes", "visibility" });

    internal static ToolSchema ListRootsArguments { get; } = ToolSchema.Object(
        description: "Arguments for listing registered reflection roots.");

    internal static ToolSchema ListRootsResult { get; } = ToolSchema.Object(
        description: "Registered reflection roots.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["roots"] = ToolSchema.Array(RootDescriptorSchema, "Registered roots."),
            ["count"] = ToolSchema.Integer("Number of roots."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "roots", "count", "capturedAtUtc" });

    internal static ToolSchema InspectObjectArguments { get; } = ToolSchema.Object(
        description: "Arguments for inspecting a registered live object.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Registered root identifier."),
            ["path"] = ToolSchema.String("Optional nested member or collection path.", nullable: true),
            ["maxDepth"] = ToolSchema.Integer("Maximum recursive expansion depth."),
            ["maxItemsPerCollection"] = ToolSchema.Integer("Maximum array, list, or dictionary items to expand.")
        },
        required: new[] { "root" });

    internal static ToolSchema InspectObjectResult { get; } = ToolSchema.Object(
        description: "Live object inspection payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Registered root identifier."),
            ["path"] = ToolSchema.String("Resolved relative path.", nullable: true),
            ["snapshot"] = GenericObjectSchema,
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "root", "snapshot", "capturedAtUtc" });

    internal static ToolSchema DescribeTypeArguments { get; } = ToolSchema.Object(
        description: "Arguments for describing a runtime type.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["typeName"] = ToolSchema.String("Runtime type name."),
            ["assemblyName"] = ToolSchema.String("Optional assembly name.", nullable: true)
        },
        required: new[] { "typeName" });

    internal static ToolSchema DescribeTypeResult { get; } = ToolSchema.Object(
        description: "Type metadata payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["typeName"] = ToolSchema.String("Resolved runtime type name."),
            ["assemblyName"] = ToolSchema.String("Resolved assembly name."),
            ["namespace"] = ToolSchema.String("Type namespace.", nullable: true),
            ["kind"] = ToolSchema.String("Type category."),
            ["baseType"] = ToolSchema.String("Base type name.", nullable: true),
            ["interfaces"] = ToolSchema.Array(ToolSchema.String("Implemented interface type name."), "Implemented interfaces."),
            ["genericArity"] = ToolSchema.Integer("Generic type arity."),
            ["memberVisibility"] = ToolSchema.String("Visibility rule applied.", enumValues: new[] { "PublicOnly", "PublicAndNonPublic" }),
            ["members"] = ToolSchema.Array(TypeMemberSchema, "Visible fields and properties."),
            ["methods"] = ToolSchema.Array(MethodSchema, "Visible methods."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "typeName", "assemblyName", "kind", "interfaces", "genericArity", "memberVisibility", "members", "methods", "capturedAtUtc" });

    internal static ToolSchema SetMemberValueArguments { get; } = ToolSchema.Object(
        description: "Arguments for setting a writable live member.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Registered root identifier."),
            ["path"] = ToolSchema.String("Relative member path to write."),
            ["valueJson"] = ToolSchema.String("JSON-encoded replacement value.")
        },
        required: new[] { "root", "path", "valueJson" });

    internal static ToolSchema SetMemberValueResult { get; } = ToolSchema.Object(
        description: "Member write payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Registered root identifier."),
            ["path"] = ToolSchema.String("Written member path."),
            ["updated"] = ToolSchema.Boolean("Whether the value was updated."),
            ["snapshot"] = GenericObjectSchema,
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "root", "path", "updated", "snapshot", "capturedAtUtc" });

    internal static ToolSchema InvokeMethodArguments { get; } = ToolSchema.Object(
        description: "Arguments for invoking an instance method reachable from a registered root.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Registered root identifier."),
            ["targetPath"] = ToolSchema.String("Optional relative path to the invocation target object.", nullable: true),
            ["method"] = ToolSchema.String("Method name."),
            ["parameterTypesJson"] = ToolSchema.String("Optional JSON array of parameter type names.", nullable: true),
            ["argumentsJson"] = ToolSchema.String("Optional JSON array of method arguments.", nullable: true)
        },
        required: new[] { "root", "method" });

    internal static ToolSchema InvokeMethodResult { get; } = ToolSchema.Object(
        description: "Method invocation payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["root"] = ToolSchema.String("Registered root identifier."),
            ["targetPath"] = ToolSchema.String("Invocation target path.", nullable: true),
            ["signature"] = ToolSchema.String("Canonical invoked method signature."),
            ["invoked"] = ToolSchema.Boolean("Whether the method was invoked."),
            ["returnSnapshot"] = GenericObjectSchema,
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "root", "signature", "invoked", "returnSnapshot", "capturedAtUtc" });
}
