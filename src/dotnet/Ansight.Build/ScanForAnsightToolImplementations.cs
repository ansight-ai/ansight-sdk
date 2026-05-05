using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Task = Microsoft.Build.Utilities.Task;

namespace Ansight.Build.Tasks;

public sealed class ScanForAnsightToolImplementations : Task
{
    private const string AnsightAssemblyName = "Ansight.Core";
    private const string ToolInterfaceFullName = "Ansight.Tools.ITool";

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public ITaskItem[] ResolverAssemblyPaths { get; set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] DetectedTools { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory) || !Directory.Exists(OutputDirectory))
        {
            DetectedTools = Array.Empty<ITaskItem>();
            return true;
        }

        var outputAssemblyPaths = Directory
            .EnumerateFiles(OutputDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (outputAssemblyPaths.Length == 0)
        {
            DetectedTools = Array.Empty<ITaskItem>();
            return true;
        }

        var assemblyResolver = BuildAssemblyResolver(outputAssemblyPaths);
        var detectedTools = new Dictionary<string, ITaskItem>(StringComparer.OrdinalIgnoreCase);
        var candidateAssemblyPaths = GetCandidateAssemblyPaths(outputAssemblyPaths, assemblyResolver);

        foreach (var assemblyPath in candidateAssemblyPaths)
        {
            TryCollectDetectedTools(assemblyPath, assemblyResolver, detectedTools);
        }

        DetectedTools = detectedTools.Values
            .OrderBy(static item => item.GetMetadata("AssemblyName"), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.ItemSpec, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return !Log.HasLoggedErrors;
    }

    private IAssemblyResolver BuildAssemblyResolver(IEnumerable<string> outputAssemblyPaths)
    {
        var searchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblyResolver = new DefaultAssemblyResolver();

        foreach (var outputAssemblyPath in outputAssemblyPaths)
        {
            TryAddSearchDirectory(Path.GetDirectoryName(outputAssemblyPath), searchDirectories, assemblyResolver);
        }

        foreach (var item in ResolverAssemblyPaths)
        {
            if (item is null)
            {
                continue;
            }

            var path = item.ItemSpec;

            if (!string.IsNullOrWhiteSpace(path))
            {
                TryAddSearchDirectory(Path.GetDirectoryName(path), searchDirectories, assemblyResolver);
            }
        }

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        TryAddSearchDirectory(runtimeDirectory, searchDirectories, assemblyResolver);

        return assemblyResolver;
    }

    private IReadOnlyList<string> GetCandidateAssemblyPaths(
        IEnumerable<string> assemblyPaths,
        IAssemblyResolver assemblyResolver)
    {
        var assembliesByPath = new Dictionary<string, AssemblyReferenceInfo>(StringComparer.OrdinalIgnoreCase);
        var reverseReferences = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingAssemblyNames = new Queue<string>();
        var seenAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyPath in assemblyPaths)
        {
            var referenceInfo = TryReadAssemblyReferences(assemblyPath, assemblyResolver);

            if (referenceInfo is null)
            {
                continue;
            }

            assembliesByPath[assemblyPath] = referenceInfo;

            if (referenceInfo.AssemblyName.Equals(AnsightAssemblyName, StringComparison.OrdinalIgnoreCase) ||
                referenceInfo.DirectReferences.Contains(AnsightAssemblyName))
            {
                candidatePaths.Add(assemblyPath);

                if (seenAssemblyNames.Add(referenceInfo.AssemblyName))
                {
                    pendingAssemblyNames.Enqueue(referenceInfo.AssemblyName);
                }
            }

            foreach (var referenceName in referenceInfo.DirectReferences)
            {
                if (!reverseReferences.TryGetValue(referenceName, out var referencingPaths))
                {
                    referencingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    reverseReferences[referenceName] = referencingPaths;
                }

                referencingPaths.Add(assemblyPath);
            }
        }

        while (pendingAssemblyNames.Count > 0)
        {
            var assemblyName = pendingAssemblyNames.Dequeue();

            if (!reverseReferences.TryGetValue(assemblyName, out var referencingPaths))
            {
                continue;
            }

            foreach (var referencingPath in referencingPaths)
            {
                if (!candidatePaths.Add(referencingPath) ||
                    !assembliesByPath.TryGetValue(referencingPath, out var referenceInfo) ||
                    !seenAssemblyNames.Add(referenceInfo.AssemblyName))
                {
                    continue;
                }

                pendingAssemblyNames.Enqueue(referenceInfo.AssemblyName);
            }
        }

        return candidatePaths
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private AssemblyReferenceInfo? TryReadAssemblyReferences(string assemblyPath, IAssemblyResolver assemblyResolver)
    {
        AssemblyDefinition assembly;

        try
        {
            assembly = AssemblyDefinition.ReadAssembly(
                assemblyPath,
                new ReaderParameters
                {
                    AssemblyResolver = assemblyResolver,
                    ReadSymbols = false
                });
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (FileLoadException exception)
        {
            Log.LogMessage(
                MessageImportance.Low,
                $"Ansight skipped assembly '{assemblyPath}' while reading references for tool scanning: {exception.Message}");
            return null;
        }

        using (assembly)
        {
            return new AssemblyReferenceInfo(
                assembly.Name?.Name ?? Path.GetFileNameWithoutExtension(assemblyPath),
                assembly.MainModule.AssemblyReferences
                    .Select(static reference => reference.Name)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    private void TryCollectDetectedTools(
        string assemblyPath,
        IAssemblyResolver assemblyResolver,
        IDictionary<string, ITaskItem> detectedTools)
    {
        AssemblyDefinition assembly;

        try
        {
            assembly = AssemblyDefinition.ReadAssembly(
                assemblyPath,
                new ReaderParameters
                {
                    AssemblyResolver = assemblyResolver,
                    ReadSymbols = false
                });
        }
        catch (BadImageFormatException)
        {
            return;
        }
        catch (FileLoadException exception)
        {
            Log.LogMessage(
                MessageImportance.Low,
                $"Ansight skipped assembly '{assemblyPath}' while scanning for tools: {exception.Message}");
            return;
        }
        catch (AssemblyResolutionException exception)
        {
            Log.LogMessage(
                MessageImportance.Low,
                $"Ansight skipped assembly '{assemblyPath}' while resolving dependencies for tool scanning: {exception.Message}");
            return;
        }

        using (assembly)
        {
            foreach (var type in assembly.MainModule.GetTypes())
            {
                if (!IsConcreteToolImplementation(type))
                {
                    continue;
                }

                var assemblyName = assembly.Name?.Name ?? Path.GetFileNameWithoutExtension(assemblyPath);
                var typeName = type.FullName ?? type.Name;
                var identity = $"{assemblyName}:{typeName}";

                if (detectedTools.ContainsKey(identity))
                {
                    continue;
                }

                var item = new TaskItem(typeName);
                item.SetMetadata("AssemblyName", assemblyName);
                item.SetMetadata("AssemblyPath", assemblyPath);
                detectedTools[identity] = item;
            }
        }
    }

    private static void TryAddSearchDirectory(
        string? directory,
        ISet<string> searchDirectories,
        BaseAssemblyResolver assemblyResolver)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (searchDirectories.Add(directory!))
        {
            assemblyResolver.AddSearchDirectory(directory!);
        }
    }

    private static bool IsConcreteToolImplementation(TypeDefinition type)
    {
        if (!type.IsClass || type.IsAbstract)
        {
            return false;
        }

        return ImplementsToolInterface(type, new HashSet<string>(StringComparer.Ordinal));
    }

    private static bool ImplementsToolInterface(TypeDefinition type, ISet<string> visitedTypes)
    {
        if (!visitedTypes.Add(type.FullName))
        {
            return false;
        }

        foreach (var implementedInterface in type.Interfaces)
        {
            if (IsToolInterface(implementedInterface.InterfaceType, new HashSet<string>(StringComparer.Ordinal)))
            {
                return true;
            }
        }

        var baseType = Resolve(type.BaseType);

        return baseType is not null && ImplementsToolInterface(baseType, visitedTypes);
    }

    private static bool IsToolInterface(TypeReference interfaceType, ISet<string> visitedTypes)
    {
        if (interfaceType.FullName == ToolInterfaceFullName)
        {
            return true;
        }

        var resolvedInterface = Resolve(interfaceType);

        if (resolvedInterface is null || !visitedTypes.Add(resolvedInterface.FullName))
        {
            return false;
        }

        if (resolvedInterface.FullName == ToolInterfaceFullName)
        {
            return true;
        }

        foreach (var inheritedInterface in resolvedInterface.Interfaces)
        {
            if (IsToolInterface(inheritedInterface.InterfaceType, visitedTypes))
            {
                return true;
            }
        }

        return false;
    }

    private static TypeDefinition? Resolve(TypeReference? typeReference)
    {
        if (typeReference is null)
        {
            return null;
        }

        try
        {
            return typeReference.Resolve();
        }
        catch
        {
            return null;
        }
    }

    private sealed class AssemblyReferenceInfo
    {
        public AssemblyReferenceInfo(string assemblyName, IReadOnlyCollection<string> directReferences)
        {
            AssemblyName = assemblyName;
            DirectReferences = directReferences;
        }

        public string AssemblyName { get; }

        public IReadOnlyCollection<string> DirectReferences { get; }
    }
}
