namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Reflection;
using System.Xml;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    internal static bool TryCreateXamlRoot(string xaml, string? rootTypeName, out BindableObject? root, out string? error)
    {
        root = null;

        if (!TryResolveXamlRootType(xaml, rootTypeName, out var rootType, out error) || rootType == null)
        {
            return false;
        }

        if (!typeof(BindableObject).IsAssignableFrom(rootType))
        {
            error = $"The XAML root type '{GetTypeDisplayName(rootType)}' is not a BindableObject.";
            return false;
        }

        if (rootType.IsAbstract)
        {
            error = $"The XAML root type '{GetTypeDisplayName(rootType)}' is abstract.";
            return false;
        }

        try
        {
            root = (BindableObject?)Activator.CreateInstance(rootType);
        }
        catch (Exception exception)
        {
            error = $"Could not create XAML root type '{GetTypeDisplayName(rootType)}': {exception.Message}";
            return false;
        }

        if (root == null)
        {
            error = $"Could not create XAML root type '{GetTypeDisplayName(rootType)}'.";
            return false;
        }

        return true;
    }

    internal static bool TryResolveXamlRootType(string xaml, string? rootTypeName, out Type? rootType, out string? error)
    {
        rootType = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(rootTypeName))
        {
            rootType = ResolveTypeName(rootTypeName);
            if (rootType == null)
            {
                error = $"The root type '{rootTypeName}' could not be resolved.";
                return false;
            }

            return true;
        }

        if (!TryReadXamlRootName(xaml, out var localName, out var namespaceName, out error))
        {
            return false;
        }

        if (IsMauiXamlNamespace(namespaceName))
        {
            rootType = ResolveTypeName($"Microsoft.Maui.Controls.{localName}")
                ?? ResolveTypeName($"Microsoft.Maui.Controls.Shapes.{localName}");
            if (rootType == null)
            {
                error = $"The MAUI XAML root element '{localName}' could not be resolved. Pass rootTypeName to disambiguate it.";
                return false;
            }

            return true;
        }

        if (TryParseClrNamespace(namespaceName, out var clrNamespace, out var assemblyName))
        {
            rootType = ResolveTypeName($"{clrNamespace}.{localName}", assemblyName);
            if (rootType == null)
            {
                error = $"The XAML root type '{clrNamespace}.{localName}' could not be resolved.";
                return false;
            }

            return true;
        }

        error = $"The XAML root namespace '{namespaceName}' is not supported. Pass rootTypeName to specify the root CLR type.";
        return false;
    }

    internal static bool TryReadXamlRootName(string xaml, out string localName, out string namespaceName, out string? error)
    {
        localName = string.Empty;
        namespaceName = string.Empty;
        error = null;

        try
        {
            using var stringReader = new StringReader(xaml);
            using var reader = XmlReader.Create(
                stringReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                localName = reader.LocalName;
                namespaceName = reader.NamespaceURI;
                return true;
            }
        }
        catch (Exception exception)
        {
            error = $"The XAML root element could not be read: {exception.Message}";
            return false;
        }

        error = "The XAML document does not contain a root element.";
        return false;
    }

    internal static bool IsMauiXamlNamespace(string namespaceName)
        => string.Equals(namespaceName, "http://schemas.microsoft.com/dotnet/2021/maui", StringComparison.Ordinal) ||
           string.Equals(namespaceName, "http://xamarin.com/schemas/2014/forms", StringComparison.Ordinal);

    internal static bool TryParseClrNamespace(string namespaceName, out string clrNamespace, out string? assemblyName)
    {
        clrNamespace = string.Empty;
        assemblyName = null;

        if (!namespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = namespaceName.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        clrNamespace = parts[0]["clr-namespace:".Length..];
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("assembly=", StringComparison.Ordinal))
            {
                assemblyName = part["assembly=".Length..];
            }
        }

        return !string.IsNullOrWhiteSpace(clrNamespace);
    }

    internal static Type? ResolveTypeName(string typeName, string? assemblyName = null)
    {
        var normalizedTypeName = typeName.Trim();
        var directType = Type.GetType(normalizedTypeName, throwOnError: false, ignoreCase: true);
        if (directType != null)
        {
            return directType;
        }

        var candidates = new List<string> { normalizedTypeName };
        if (!normalizedTypeName.Contains('.', StringComparison.Ordinal))
        {
            candidates.Add($"Microsoft.Maui.Controls.{normalizedTypeName}");
            candidates.Add($"Microsoft.Maui.Controls.Shapes.{normalizedTypeName}");
        }

        foreach (var assembly in GetCandidateAssemblies(assemblyName))
        {
            foreach (var candidate in candidates)
            {
                var candidateType = assembly.GetType(candidate, throwOnError: false, ignoreCase: true);
                if (candidateType != null)
                {
                    return candidateType;
                }
            }
        }

        if (normalizedTypeName.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var assembly in GetCandidateAssemblies(assemblyName))
        {
            var candidateType = GetLoadableTypes(assembly)
                .FirstOrDefault(type => string.Equals(type.Name, normalizedTypeName, StringComparison.OrdinalIgnoreCase));
            if (candidateType != null)
            {
                return candidateType;
            }
        }

        return null;
    }

    internal static IEnumerable<Assembly> GetCandidateAssemblies(string? assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            if (loadedAssembly != null)
            {
                yield return loadedAssembly;
                yield break;
            }

            Assembly? resolvedAssembly = null;
            try
            {
                resolvedAssembly = Assembly.Load(new AssemblyName(assemblyName));
            }
            catch
            {
            }

            if (resolvedAssembly != null)
            {
                yield return resolvedAssembly;
            }

            yield break;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            yield return assembly;
        }
    }

    internal static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    internal static bool TryAttachElement(
        Element parent,
        Element child,
        int? index,
        bool replaceContent,
        out string? container,
        out string? error)
    {
        container = null;
        error = null;

        if (parent is Layout layout)
        {
            if (child is not IView childView)
            {
                error = $"The child '{GetTypeDisplayName(child.GetType())}' cannot be added to a Layout because it is not an IView.";
                return false;
            }

            if (index.HasValue)
            {
                if (index.Value < 0 || index.Value > layout.Children.Count)
                {
                    error = $"The index {index.Value} is outside the valid range 0..{layout.Children.Count}.";
                    return false;
                }

                layout.Children.Insert(index.Value, childView);
            }
            else
            {
                layout.Children.Add(childView);
            }

            container = "Children";
            return true;
        }

        if (index.HasValue)
        {
            error = "The index argument is only supported when adding to a Layout Children collection.";
            return false;
        }

        var contentProperty = ResolvePublicInstanceProperty(parent.GetType(), "Content");
        if (contentProperty == null || !HasPublicSetter(contentProperty))
        {
            error = $"The parent '{GetTypeDisplayName(parent.GetType())}' does not expose a supported Children collection or writable Content property.";
            return false;
        }

        if (!contentProperty.PropertyType.IsAssignableFrom(child.GetType()))
        {
            error = $"The parent Content property expects '{GetTypeDisplayName(contentProperty.PropertyType)}', but the child is '{GetTypeDisplayName(child.GetType())}'.";
            return false;
        }

        var existingContent = contentProperty.GetValue(parent);
        if (existingContent != null && !ReferenceEquals(existingContent, child) && !replaceContent)
        {
            error = $"The parent '{GetTypeDisplayName(parent.GetType())}' already has Content. Pass replaceContent=true to replace it.";
            return false;
        }

        contentProperty.SetValue(parent, child);
        container = contentProperty.Name;
        return true;
    }

    internal static bool TryDetachElement(Element child, out Element? parent, out string? container, out string? error)
    {
        parent = child.Parent;
        container = null;
        error = null;

        if (parent == null)
        {
            return true;
        }

        if (parent is Layout layout && child is IView childView)
        {
            if (layout.Children.Remove(childView))
            {
                container = "Children";
                return true;
            }
        }

        var contentProperty = ResolvePublicInstanceProperty(parent.GetType(), "Content");
        if (contentProperty != null && HasPublicSetter(contentProperty))
        {
            var existingContent = contentProperty.GetValue(parent);
            if (ReferenceEquals(existingContent, child))
            {
                contentProperty.SetValue(parent, null);
                container = contentProperty.Name;
                return true;
            }
        }

        error = $"The child '{GetTypeDisplayName(child.GetType())}' is parented by '{GetTypeDisplayName(parent.GetType())}', but no supported detach path was found.";
        return false;
    }
}
#endif
