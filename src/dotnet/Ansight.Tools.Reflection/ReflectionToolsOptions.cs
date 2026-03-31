namespace Ansight.Tools.Reflection;

public sealed class ReflectionToolsOptions
{
    internal static ReflectionToolsOptions Default { get; } = new(
        Array.Empty<ReflectionRootRegistration>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        ReflectionAssemblyTraversalMode.AllowListedOnly,
        ReflectionNamespaceTraversalMode.AllowListedOnly,
        ReflectionMemberVisibility.PublicOnly);

    internal ReflectionToolsOptions(
        IReadOnlyList<ReflectionRootRegistration> roots,
        IReadOnlyCollection<string> allowedAssemblies,
        IReadOnlyCollection<string> allowedNamespacePrefixes,
        ReflectionAssemblyTraversalMode assemblyTraversalMode,
        ReflectionNamespaceTraversalMode namespaceTraversalMode,
        ReflectionMemberVisibility defaultMemberVisibility)
    {
        Roots = roots;
        AllowedAssemblies = allowedAssemblies;
        AllowedNamespacePrefixes = allowedNamespacePrefixes;
        AssemblyTraversalMode = assemblyTraversalMode;
        NamespaceTraversalMode = namespaceTraversalMode;
        DefaultMemberVisibility = defaultMemberVisibility;
    }

    internal IReadOnlyList<ReflectionRootRegistration> Roots { get; }

    public IReadOnlyCollection<string> AllowedAssemblies { get; }

    public IReadOnlyCollection<string> AllowedNamespacePrefixes { get; }

    public ReflectionAssemblyTraversalMode AssemblyTraversalMode { get; }

    public ReflectionNamespaceTraversalMode NamespaceTraversalMode { get; }

    public ReflectionMemberVisibility DefaultMemberVisibility { get; }

    public static ReflectionToolsOptionsBuilder CreateBuilder() => new();
}

public enum ReflectionAssemblyTraversalMode
{
    AllowListedOnly = 0,
    AllowAll = 1
}

public enum ReflectionNamespaceTraversalMode
{
    AllowListedOnly = 0,
    AllowAll = 1
}

public sealed class ReflectionToolsOptionsBuilder
{
    private readonly Dictionary<string, ReflectionRootRegistration> rootsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> allowedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> allowedNamespacePrefixes = new(StringComparer.Ordinal);
    private ReflectionAssemblyTraversalMode assemblyTraversalMode = ReflectionAssemblyTraversalMode.AllowListedOnly;
    private ReflectionNamespaceTraversalMode namespaceTraversalMode = ReflectionNamespaceTraversalMode.AllowListedOnly;
    private ReflectionMemberVisibility defaultMemberVisibility = ReflectionMemberVisibility.PublicOnly;

    public ReflectionToolsOptionsBuilder AddRoot(
        string id,
        object target,
        ReflectionRootMetadata metadata,
        Action<ReflectionRootBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(metadata);

        if (target.GetType().IsValueType)
        {
            throw new ArgumentException("Direct reflection roots must be reference types.", nameof(target));
        }

        var reference = new WeakReference<object>(target);
        return AddRootCore(
            id,
            metadata,
            ReflectionRootRegistrationKind.Reference,
            ReflectionReferenceStrength.Weak,
            () =>
            {
                return reference.TryGetTarget(out var resolved)
                    ? resolved
                    : null;
            },
            configure);
    }

    public ReflectionToolsOptionsBuilder AddStrongRoot(
        string id,
        object target,
        ReflectionRootMetadata metadata,
        Action<ReflectionRootBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(metadata);

        if (target.GetType().IsValueType)
        {
            throw new ArgumentException("Direct reflection roots must be reference types.", nameof(target));
        }

        return AddRootCore(
            id,
            metadata,
            ReflectionRootRegistrationKind.Reference,
            ReflectionReferenceStrength.Strong,
            () => target,
            configure);
    }

    public ReflectionToolsOptionsBuilder AddRoot(
        string id,
        Func<object?> resolver,
        ReflectionRootMetadata metadata,
        Action<ReflectionRootBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(metadata);

        return AddRootCore(
            id,
            metadata,
            ReflectionRootRegistrationKind.Delegate,
            referenceStrength: null,
            resolver,
            configure);
    }

    public ReflectionToolsOptionsBuilder AllowAssembly(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        allowedAssemblies.Add(assemblyName.Trim());
        return this;
    }

    public ReflectionToolsOptionsBuilder AllowAssemblies(params string[] assemblyNames)
    {
        ArgumentNullException.ThrowIfNull(assemblyNames);

        foreach (var assemblyName in assemblyNames)
        {
            AllowAssembly(assemblyName);
        }

        return this;
    }

    public ReflectionToolsOptionsBuilder AllowNamespacePrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        allowedNamespacePrefixes.Add(prefix.Trim());
        return this;
    }

    public ReflectionToolsOptionsBuilder AllowNamespacePrefixes(params string[] prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);

        foreach (var prefix in prefixes)
        {
            AllowNamespacePrefix(prefix);
        }

        return this;
    }

    public ReflectionToolsOptionsBuilder WithAssemblyTraversalMode(ReflectionAssemblyTraversalMode mode)
    {
        assemblyTraversalMode = mode;
        return this;
    }

    public ReflectionToolsOptionsBuilder WithNamespaceTraversalMode(ReflectionNamespaceTraversalMode mode)
    {
        namespaceTraversalMode = mode;
        return this;
    }

    public ReflectionToolsOptionsBuilder WithDefaultMemberVisibility(ReflectionMemberVisibility visibility)
    {
        defaultMemberVisibility = visibility;
        return this;
    }

    public ReflectionToolsOptions Build()
        => new(
            rootsById.Values.OrderBy(root => root.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            allowedAssemblies.ToArray(),
            allowedNamespacePrefixes.ToArray(),
            assemblyTraversalMode,
            namespaceTraversalMode,
            defaultMemberVisibility);

    private ReflectionToolsOptionsBuilder AddRootCore(
        string id,
        ReflectionRootMetadata metadata,
        ReflectionRootRegistrationKind registrationKind,
        ReflectionReferenceStrength? referenceStrength,
        Func<object?> resolver,
        Action<ReflectionRootBuilder>? configure)
    {
        ValidateMetadata(metadata);

        var builder = new ReflectionRootBuilder();
        configure?.Invoke(builder);
        var registration = builder.Build(
            id.Trim(),
            NormalizeMetadata(metadata),
            registrationKind,
            referenceStrength,
            resolver);

        rootsById[registration.Id] = registration;
        return this;
    }

    private static ReflectionRootMetadata NormalizeMetadata(ReflectionRootMetadata metadata)
    {
        return metadata with
        {
            DisplayName = metadata.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(metadata.Description) ? null : metadata.Description.Trim(),
            Hints = metadata.Hints
                .Where(hint => !string.IsNullOrWhiteSpace(hint))
                .Select(hint => hint.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static void ValidateMetadata(ReflectionRootMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.DisplayName))
        {
            throw new ArgumentException("Reflection root metadata must include a non-empty display name.", nameof(metadata));
        }
    }
}

public sealed class ReflectionRootBuilder
{
    private readonly HashSet<string> allowedWritableMembers = new(StringComparer.Ordinal);
    private readonly HashSet<string> allowedInvokableMethods = new(StringComparer.Ordinal);
    private readonly HashSet<Type> allowedWritableTypes = new();
    private readonly HashSet<Type> allowedInvokableTypes = new();
    private bool allowAllWritableMembers;
    private bool allowAllInvokableMethods;
    private ReflectionMemberVisibility? memberVisibility;

    public ReflectionRootBuilder WithMemberVisibility(ReflectionMemberVisibility visibility)
    {
        memberVisibility = visibility;
        return this;
    }

    public ReflectionRootBuilder AllowWritableMembers(params string[] memberPaths)
    {
        ArgumentNullException.ThrowIfNull(memberPaths);

        foreach (var memberPath in memberPaths)
        {
            if (string.IsNullOrWhiteSpace(memberPath))
            {
                continue;
            }

            allowedWritableMembers.Add(memberPath.Trim());
        }

        return this;
    }

    public ReflectionRootBuilder AllowAllWritableMembers()
    {
        allowAllWritableMembers = true;
        return this;
    }

    public ReflectionRootBuilder AllowAllWritableMembersOn(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type);
            allowedWritableTypes.Add(type);
        }

        return this;
    }

    public ReflectionRootBuilder AllowAllWritableMembersOn<T>()
        => AllowAllWritableMembersOn(typeof(T));

    public ReflectionRootBuilder AllowInvokableMethods(params string[] methodSignatures)
    {
        ArgumentNullException.ThrowIfNull(methodSignatures);

        foreach (var methodSignature in methodSignatures)
        {
            if (string.IsNullOrWhiteSpace(methodSignature))
            {
                continue;
            }

            allowedInvokableMethods.Add(methodSignature.Trim());
        }

        return this;
    }

    public ReflectionRootBuilder AllowAllInvokableMethods()
    {
        allowAllInvokableMethods = true;
        return this;
    }

    public ReflectionRootBuilder AllowAllInvokableMethodsOn(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type);
            allowedInvokableTypes.Add(type);
        }

        return this;
    }

    public ReflectionRootBuilder AllowAllInvokableMethodsOn<T>()
        => AllowAllInvokableMethodsOn(typeof(T));

    internal ReflectionRootRegistration Build(
        string id,
        ReflectionRootMetadata metadata,
        ReflectionRootRegistrationKind registrationKind,
        ReflectionReferenceStrength? referenceStrength,
        Func<object?> resolver)
    {
        return new ReflectionRootRegistration(
            id,
            metadata,
            registrationKind,
            referenceStrength,
            resolver,
            memberVisibility,
            allowAllWritableMembers,
            allowedWritableMembers.ToArray(),
            allowedWritableTypes.ToArray(),
            allowAllInvokableMethods,
            allowedInvokableMethods.ToArray(),
            allowedInvokableTypes.ToArray());
    }
}

internal enum ReflectionRootRegistrationKind
{
    Reference = 0,
    Delegate = 1
}

internal enum ReflectionReferenceStrength
{
    Weak = 0,
    Strong = 1
}

internal sealed class ReflectionRootRegistration
{
    public ReflectionRootRegistration(
        string id,
        ReflectionRootMetadata metadata,
        ReflectionRootRegistrationKind registrationKind,
        ReflectionReferenceStrength? referenceStrength,
        Func<object?> resolver,
        ReflectionMemberVisibility? memberVisibility,
        bool allowAllWritableMembers,
        IReadOnlyCollection<string> allowedWritableMembers,
        IReadOnlyCollection<Type> allowedWritableTypes,
        bool allowAllInvokableMethods,
        IReadOnlyCollection<string> allowedInvokableMethods,
        IReadOnlyCollection<Type> allowedInvokableTypes)
    {
        Id = id;
        Metadata = metadata;
        RegistrationKind = registrationKind;
        ReferenceStrength = referenceStrength;
        this.resolver = resolver;
        MemberVisibility = memberVisibility;
        AllowAllWritableMembers = allowAllWritableMembers;
        AllowedWritableMembers = allowedWritableMembers;
        AllowedWritableTypes = allowedWritableTypes;
        AllowAllInvokableMethods = allowAllInvokableMethods;
        AllowedInvokableMethods = allowedInvokableMethods;
        AllowedInvokableTypes = allowedInvokableTypes;
    }

    private readonly Func<object?> resolver;

    public string Id { get; }

    public ReflectionRootMetadata Metadata { get; }

    public ReflectionRootRegistrationKind RegistrationKind { get; }

    public ReflectionReferenceStrength? ReferenceStrength { get; }

    public ReflectionMemberVisibility? MemberVisibility { get; }

    public bool AllowAllWritableMembers { get; }

    public IReadOnlyCollection<string> AllowedWritableMembers { get; }

    public IReadOnlyCollection<Type> AllowedWritableTypes { get; }

    public bool AllowAllInvokableMethods { get; }

    public IReadOnlyCollection<string> AllowedInvokableMethods { get; }

    public IReadOnlyCollection<Type> AllowedInvokableTypes { get; }

    public bool CanWriteMembers
        => AllowAllWritableMembers || AllowedWritableMembers.Count > 0 || AllowedWritableTypes.Count > 0;

    public bool CanInvokeMethods
        => AllowAllInvokableMethods || AllowedInvokableMethods.Count > 0 || AllowedInvokableTypes.Count > 0;

    public bool IsWriteAllowed(string path, Type targetType)
        => AllowAllWritableMembers ||
           AllowedWritableMembers.Contains(path) ||
           AllowedWritableTypes.Any(type => type.IsAssignableFrom(targetType));

    public bool IsInvocationAllowed(string? targetPath, string signature, Type targetType)
    {
        if (AllowAllInvokableMethods ||
            AllowedInvokableTypes.Any(type => type.IsAssignableFrom(targetType)))
        {
            return true;
        }

        var invocationKey = string.IsNullOrWhiteSpace(targetPath)
            ? signature
            : targetPath + "#" + signature;

        return AllowedInvokableMethods.Contains(invocationKey) ||
               (string.IsNullOrWhiteSpace(targetPath) && AllowedInvokableMethods.Contains(signature));
    }

    public ReflectionRootResolution Resolve()
    {
        try
        {
            var value = resolver();
            return value is null
                ? new ReflectionRootResolution(false, null, "The registered root is not currently available.")
                : new ReflectionRootResolution(true, value, null);
        }
        catch (Exception exception)
        {
            return new ReflectionRootResolution(false, null, exception.Message);
        }
    }
}

internal sealed record ReflectionRootResolution(bool Available, object? Value, string? Error);
