namespace Ansight.Tools.Reflection;

public sealed class ReflectionToolsOptions
{
    internal static ReflectionToolsOptions Default { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        ReflectionAssemblyTraversalMode.AllowAll,
        ReflectionNamespaceTraversalMode.AllowAll,
        ReflectionMemberVisibility.PublicOnly);

    internal ReflectionToolsOptions(
        IReadOnlyCollection<string> allowedAssemblies,
        IReadOnlyCollection<string> allowedNamespacePrefixes,
        ReflectionAssemblyTraversalMode assemblyTraversalMode,
        ReflectionNamespaceTraversalMode namespaceTraversalMode,
        ReflectionMemberVisibility defaultMemberVisibility)
    {
        AllowedAssemblies = allowedAssemblies;
        AllowedNamespacePrefixes = allowedNamespacePrefixes;
        AssemblyTraversalMode = assemblyTraversalMode;
        NamespaceTraversalMode = namespaceTraversalMode;
        DefaultMemberVisibility = defaultMemberVisibility;
    }

    public IReadOnlyCollection<string> AllowedAssemblies { get; }

    public IReadOnlyCollection<string> AllowedNamespacePrefixes { get; }

    public ReflectionAssemblyTraversalMode AssemblyTraversalMode { get; }

    public ReflectionNamespaceTraversalMode NamespaceTraversalMode { get; }

    public ReflectionMemberVisibility DefaultMemberVisibility { get; }

    public static ReflectionToolsOptionsBuilder CreateBuilder() => new();
}

public static class ReflectionRootRegistry
{
    private static readonly object rootsLock = new();
    private static readonly Dictionary<string, ReflectionRootRegistration> rootsById = new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<ReflectionRootRegistration> Roots
    {
        get
        {
            lock (rootsLock)
            {
                return rootsById.Values
                    .OrderBy(root => root.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public static ReflectionRootRegistrationHandle Register(
        string id,
        object target,
        ReflectionRootMetadata metadata,
        ReferenceType referenceType = ReferenceType.Weak)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(metadata);

        if (target.GetType().IsValueType)
        {
            throw new ArgumentException("Direct reflection roots must be reference types.", nameof(target));
        }

        return referenceType switch
        {
            ReferenceType.Weak => RegisterWeakReference(id, target, metadata),
            ReferenceType.Strong => RegisterCore(id, metadata, ReflectionRootRegistrationKind.StrongReference, () => target),
            _ => throw new ArgumentOutOfRangeException(nameof(referenceType), referenceType, "Unsupported reflection root reference type.")
        };
    }

    public static ReflectionRootRegistrationHandle Register(
        string id,
        Func<object?> targetGetter,
        ReflectionRootMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(targetGetter);
        ArgumentNullException.ThrowIfNull(metadata);

        return RegisterCore(id, metadata, ReflectionRootRegistrationKind.Getter, targetGetter);
    }

    public static bool Deregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (rootsLock)
        {
            return rootsById.Remove(id.Trim());
        }
    }

    internal static bool TryGetRoot(string id, out ReflectionRootRegistration? registration)
    {
        lock (rootsLock)
        {
            return rootsById.TryGetValue(id.Trim(), out registration);
        }
    }

    internal static bool Deregister(string id, Guid registrationId)
    {
        lock (rootsLock)
        {
            var normalizedId = id.Trim();
            if (!rootsById.TryGetValue(normalizedId, out var current) ||
                current.RegistrationId != registrationId)
            {
                return false;
            }

            return rootsById.Remove(normalizedId);
        }
    }

    internal static void Clear()
    {
        lock (rootsLock)
        {
            rootsById.Clear();
        }
    }

    private static ReflectionRootRegistrationHandle RegisterCore(
        string id,
        ReflectionRootMetadata metadata,
        ReflectionRootRegistrationKind kind,
        Func<object?> resolver)
    {
        var registration = CreateRootRegistration(
            id,
            metadata,
            kind,
            resolver);

        lock (rootsLock)
        {
            rootsById[registration.Id] = registration;
        }

        return new ReflectionRootRegistrationHandle(registration.Id, registration.RegistrationId);
    }

    private static ReflectionRootRegistration CreateRootRegistration(
        string id,
        ReflectionRootMetadata metadata,
        ReflectionRootRegistrationKind kind,
        Func<object?> resolver)
    {
        ValidateMetadata(metadata);

        return new ReflectionRootRegistration(
            id.Trim(),
            Guid.NewGuid(),
            NormalizeMetadata(metadata),
            kind,
            resolver);
    }

    private static ReflectionRootRegistrationHandle RegisterWeakReference(
        string id,
        object target,
        ReflectionRootMetadata metadata)
    {
        var reference = new WeakReference<object>(target);
        return RegisterCore(
            id,
            metadata,
            ReflectionRootRegistrationKind.WeakReference,
            () =>
            {
                return reference.TryGetTarget(out var resolved)
                    ? resolved
                    : null;
            });
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

public sealed class ReflectionRootRegistrationHandle : IDisposable
{
    private int deregistered;

    internal ReflectionRootRegistrationHandle(string id, Guid registrationId)
    {
        Id = id;
        RegistrationId = registrationId;
    }

    public string Id { get; }

    internal Guid RegistrationId { get; }

    public bool Deregister()
    {
        if (Interlocked.Exchange(ref deregistered, 1) != 0)
        {
            return false;
        }

        return ReflectionRootRegistry.Deregister(Id, RegistrationId);
    }

    public void Dispose()
    {
        Deregister();
    }
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

public enum ReferenceType
{
    Weak = 0,
    Strong = 1
}

public sealed class ReflectionToolsOptionsBuilder
{
    private readonly HashSet<string> allowedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> allowedNamespacePrefixes = new(StringComparer.Ordinal);
    private ReflectionAssemblyTraversalMode assemblyTraversalMode = ReflectionAssemblyTraversalMode.AllowAll;
    private ReflectionNamespaceTraversalMode namespaceTraversalMode = ReflectionNamespaceTraversalMode.AllowAll;
    private ReflectionMemberVisibility defaultMemberVisibility = ReflectionMemberVisibility.PublicOnly;

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
            allowedAssemblies.ToArray(),
            allowedNamespacePrefixes.ToArray(),
            assemblyTraversalMode,
            namespaceTraversalMode,
            defaultMemberVisibility);
}

internal sealed class ReflectionRootRegistration
{
    private readonly Func<object?> resolver;

    public ReflectionRootRegistration(
        string id,
        Guid registrationId,
        ReflectionRootMetadata metadata,
        ReflectionRootRegistrationKind kind,
        Func<object?> resolver)
    {
        Id = id;
        RegistrationId = registrationId;
        Metadata = metadata;
        Kind = kind;
        this.resolver = resolver;
    }

    public string Id { get; }

    public Guid RegistrationId { get; }

    public ReflectionRootMetadata Metadata { get; }

    public ReflectionRootRegistrationKind Kind { get; }

    public ReflectionRootResolution Resolve()
    {
        try
        {
            var value = resolver();
            if (value is null)
            {
                return new ReflectionRootResolution(false, null, "The registered root is not currently available.");
            }

            return value.GetType().IsValueType
                ? new ReflectionRootResolution(false, null, "The registered root must resolve to a reference type.")
                : new ReflectionRootResolution(true, value, null);
        }
        catch (Exception exception)
        {
            return new ReflectionRootResolution(false, null, exception.Message);
        }
    }
}

internal enum ReflectionRootRegistrationKind
{
    WeakReference = 0,
    StrongReference = 1,
    Getter = 2
}

internal sealed record ReflectionRootResolution(bool Available, object? Value, string? Error);
