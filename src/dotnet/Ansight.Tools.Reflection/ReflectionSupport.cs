namespace Ansight.Tools.Reflection;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ReflectionSupport
{
    private const int DefaultMaxDepth = 1;
    private const int DefaultMaxItemsPerCollection = 10;
    private const int MaximumMaxDepth = 4;
    private const int MaximumItemsPerCollection = 64;

    internal static JsonObject ListRoots(ReflectionToolsOptions options)
    {
        var roots = new JsonArray();
        foreach (var root in options.Roots)
        {
            var resolution = root.Resolve();
            roots.Add(new JsonObject
            {
                ["id"] = root.Id,
                ["metadata"] = ToJson(root.Metadata),
                ["registrationKind"] = root.RegistrationKind == ReflectionRootRegistrationKind.Reference ? "reference" : "delegate",
                ["referenceStrength"] = root.ReferenceStrength switch
                {
                    ReflectionReferenceStrength.Weak => "weak",
                    ReflectionReferenceStrength.Strong => "strong",
                    _ => null
                },
                ["available"] = resolution.Available,
                ["runtimeType"] = resolution.Value?.GetType().FullName,
                ["memberVisibility"] = GetEffectiveVisibility(options, root).ToString(),
                ["canWriteMembers"] = root.AllowedWritableMembers.Count > 0,
                ["canInvokeMethods"] = root.AllowedInvokableMethods.Count > 0,
                ["resolutionError"] = resolution.Available
                    ? null
                    : string.IsNullOrWhiteSpace(resolution.Error) ? null : resolution.Error
            });
        }

        return new JsonObject
        {
            ["roots"] = roots,
            ["count"] = roots.Count,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject InspectObject(ReflectionToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        var rootId = GetRequiredString(arguments, "root");
        var path = GetString(arguments, "path");
        var maxDepth = GetInt(arguments, "maxDepth", DefaultMaxDepth, minimum: 0, maximum: MaximumMaxDepth);
        var maxItemsPerCollection = GetInt(arguments, "maxItemsPerCollection", DefaultMaxItemsPerCollection, minimum: 1, maximum: MaximumItemsPerCollection);
        var registration = GetRoot(options, rootId);
        var resolution = registration.Resolve();
        if (!resolution.Available || resolution.Value == null)
        {
            throw new InvalidOperationException(resolution.Error ?? $"The root '{rootId}' is not currently available.");
        }

        var segments = ParsePath(path);
        var memberVisibility = GetEffectiveVisibility(options, registration);
        var current = ResolveTarget(resolution.Value, segments, memberVisibility);
        var state = new ReflectionSnapshotState(options, registration, maxItemsPerCollection);
        var snapshot = CreateSnapshot(
            current.Value,
            current.DeclaredType,
            path,
            Math.Max(maxDepth, 0),
            state,
            allowExpansion: true,
            isRoot: true);

        return new JsonObject
        {
            ["root"] = rootId,
            ["path"] = path,
            ["snapshot"] = snapshot,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject DescribeType(ReflectionToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        var typeName = GetRequiredString(arguments, "typeName");
        var assemblyName = GetString(arguments, "assemblyName");
        var type = ResolveType(typeName, assemblyName)
                   ?? throw new InvalidOperationException($"Type '{typeName}' could not be resolved.");
        var visibility = options.DefaultMemberVisibility;

        return new JsonObject
        {
            ["typeName"] = GetTypeDisplayName(type),
            ["assemblyName"] = type.Assembly.GetName().Name,
            ["namespace"] = type.Namespace,
            ["kind"] = GetValueKind(type, value: null),
            ["baseType"] = type.BaseType == null ? null : GetTypeDisplayName(type.BaseType),
            ["interfaces"] = CreateStringArray(type.GetInterfaces().Select(GetTypeDisplayName)),
            ["genericArity"] = type.IsGenericType ? type.GetGenericArguments().Length : 0,
            ["memberVisibility"] = visibility.ToString(),
            ["members"] = DescribeMembers(type, visibility),
            ["methods"] = DescribeMethods(type, visibility, allowedInvocations: null, targetPath: null),
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject SetMemberValue(ReflectionToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        var rootId = GetRequiredString(arguments, "root");
        var path = GetRequiredString(arguments, "path");
        var valueJson = GetRequiredString(arguments, "valueJson");
        var registration = GetRoot(options, rootId);
        if (!registration.AllowedWritableMembers.Contains(path))
        {
            throw new InvalidOperationException($"Member path '{path}' is not allowed for writes on root '{rootId}'.");
        }

        var resolution = registration.Resolve();
        if (!resolution.Available || resolution.Value == null)
        {
            throw new InvalidOperationException(resolution.Error ?? $"The root '{rootId}' is not currently available.");
        }

        var segments = ParsePath(path);
        if (segments.Count == 0 || segments[^1].Kind != ReflectionPathSegmentKind.Member)
        {
            throw new InvalidOperationException("Write paths must end on a field or property.");
        }

        var memberVisibility = GetEffectiveVisibility(options, registration);
        var parentSegments = segments.Take(segments.Count - 1).ToList();
        var parent = ResolveTarget(resolution.Value, parentSegments, memberVisibility);
        if (parent.Value == null)
        {
            throw new InvalidOperationException($"Parent object for path '{path}' resolved to null.");
        }

        var memberName = segments[^1].Value!;
        var member = FindMember(parent.Value.GetType(), memberName, memberVisibility)
                     ?? throw new InvalidOperationException($"Member '{memberName}' was not found.");

        if (member is PropertyInfo property)
        {
            if (!property.CanWrite || property.SetMethod == null)
            {
                throw new InvalidOperationException($"Property '{memberName}' is not writable.");
            }

            var convertedValue = ConvertValue(ParseJsonArgument(valueJson), property.PropertyType);
            property.SetValue(parent.Value, convertedValue);
            return CreateMutationResult(rootId, path, parent.Value, property.PropertyType, updated: true);
        }

        if (member is FieldInfo field)
        {
            if (field.IsInitOnly)
            {
                throw new InvalidOperationException($"Field '{memberName}' is read-only.");
            }

            var convertedValue = ConvertValue(ParseJsonArgument(valueJson), field.FieldType);
            field.SetValue(parent.Value, convertedValue);
            return CreateMutationResult(rootId, path, parent.Value, field.FieldType, updated: true);
        }

        throw new InvalidOperationException($"Member '{memberName}' is not writable.");
    }

    internal static JsonObject InvokeMethod(ReflectionToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        var rootId = GetRequiredString(arguments, "root");
        var targetPath = GetString(arguments, "targetPath");
        var methodName = GetRequiredString(arguments, "method");
        var parameterTypeNames = ParseStringArrayArgument(GetString(arguments, "parameterTypesJson"));
        var argumentValues = ParseJsonArrayArgument(GetString(arguments, "argumentsJson"));
        var registration = GetRoot(options, rootId);
        var resolution = registration.Resolve();
        if (!resolution.Available || resolution.Value == null)
        {
            throw new InvalidOperationException(resolution.Error ?? $"The root '{rootId}' is not currently available.");
        }

        var memberVisibility = GetEffectiveVisibility(options, registration);
        var target = string.IsNullOrWhiteSpace(targetPath)
            ? new ReflectionResolvedValue(resolution.Value, resolution.Value.GetType())
            : ResolveTarget(resolution.Value, ParsePath(targetPath), memberVisibility);

        if (target.Value == null)
        {
            throw new InvalidOperationException("Invocation target resolved to null.");
        }

        var method = ResolveMethod(target.Value.GetType(), methodName, parameterTypeNames, memberVisibility);
        if (method.IsStatic)
        {
            throw new InvalidOperationException("Static method invocation is not supported.");
        }

        var methodSignature = CreateMethodSignature(method);
        var invocationKey = CreateInvocationKey(targetPath, methodSignature);
        if (!registration.AllowedInvokableMethods.Contains(invocationKey) &&
            !(string.IsNullOrWhiteSpace(targetPath) && registration.AllowedInvokableMethods.Contains(methodSignature)))
        {
            throw new InvalidOperationException($"Method '{invocationKey}' is not allowed for invocation on root '{rootId}'.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != argumentValues.Count)
        {
            throw new InvalidOperationException($"Method '{methodSignature}' expects {parameters.Length} argument(s) but received {argumentValues.Count}.");
        }

        var convertedArguments = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            convertedArguments[index] = ConvertValue(argumentValues[index], parameters[index].ParameterType);
        }

        object? returnValue;
        try
        {
            returnValue = method.Invoke(target.Value, convertedArguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException(exception.InnerException.Message, exception.InnerException);
        }

        var state = new ReflectionSnapshotState(options, registration, DefaultMaxItemsPerCollection);
        var returnSnapshot = method.ReturnType == typeof(void)
            ? CreateVoidSnapshot()
            : CreateSnapshot(returnValue, method.ReturnType, path: null, depthRemaining: 1, state, allowExpansion: true, isRoot: false);

        return new JsonObject
        {
            ["root"] = rootId,
            ["targetPath"] = targetPath,
            ["signature"] = methodSignature,
            ["invoked"] = true,
            ["returnSnapshot"] = returnSnapshot,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    private static JsonObject CreateMutationResult(string rootId, string path, object value, Type declaredType, bool updated)
    {
        var registration = new ReflectionRootRegistration(
            "__mutation__",
            new ReflectionRootMetadata("mutation"),
            ReflectionRootRegistrationKind.Reference,
            ReflectionReferenceStrength.Strong,
            () => value,
            ReflectionMemberVisibility.PublicAndNonPublic,
            Array.Empty<string>(),
            Array.Empty<string>());
        var state = new ReflectionSnapshotState(ReflectionToolsOptions.Default, registration, DefaultMaxItemsPerCollection);
        return new JsonObject
        {
            ["root"] = rootId,
            ["path"] = path,
            ["updated"] = updated,
            ["snapshot"] = CreateSnapshot(value, declaredType, path: null, depthRemaining: 1, state, allowExpansion: true, isRoot: false),
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    private static JsonObject CreateVoidSnapshot()
    {
        return new JsonObject
        {
            ["kind"] = "void",
            ["isNull"] = true,
            ["preview"] = "void",
            ["expandable"] = false
        };
    }

    private static ReflectionRootRegistration GetRoot(ReflectionToolsOptions options, string rootId)
    {
        var root = options.Roots.SingleOrDefault(candidate => string.Equals(candidate.Id, rootId, StringComparison.OrdinalIgnoreCase));
        return root ?? throw new InvalidOperationException($"Reflection root '{rootId}' is not registered.");
    }

    private static ReflectionMemberVisibility GetEffectiveVisibility(ReflectionToolsOptions options, ReflectionRootRegistration registration)
        => registration.MemberVisibility ?? options.DefaultMemberVisibility;

    private static JsonObject ToJson(ReflectionRootMetadata metadata)
    {
        var tags = new JsonArray();
        foreach (var tag in metadata.Tags)
        {
            tags.Add(tag);
        }

        var attributes = new JsonObject();
        foreach (var pair in metadata.Attributes)
        {
            attributes[pair.Key] = pair.Value;
        }

        return new JsonObject
        {
            ["displayName"] = metadata.DisplayName,
            ["description"] = metadata.Description,
            ["category"] = metadata.Category,
            ["tags"] = tags,
            ["containsSensitiveData"] = metadata.ContainsSensitiveData,
            ["attributes"] = attributes
        };
    }

    private static JsonArray DescribeMembers(Type type, ReflectionMemberVisibility visibility)
    {
        var results = new JsonArray();
        foreach (var member in EnumerateMembers(type, visibility))
        {
            results.Add(new JsonObject
            {
                ["name"] = member.Name,
                ["memberType"] = member.MemberType == MemberTypes.Field ? "field" : "property",
                ["declaringType"] = GetTypeDisplayName(member.DeclaringType!),
                ["type"] = member switch
                {
                    FieldInfo field => GetTypeDisplayName(field.FieldType),
                    PropertyInfo property => GetTypeDisplayName(property.PropertyType),
                    _ => string.Empty
                },
                ["readable"] = member is FieldInfo || (member as PropertyInfo)?.CanRead == true,
                ["writable"] = member switch
                {
                    FieldInfo field => !field.IsInitOnly,
                    PropertyInfo property => property.CanWrite && property.SetMethod != null,
                    _ => false
                },
                ["visibility"] = IsPublic(member) ? "public" : "non_public"
            });
        }

        return results;
    }

    private static JsonArray DescribeMethods(
        Type type,
        ReflectionMemberVisibility visibility,
        IReadOnlyCollection<string>? allowedInvocations,
        string? targetPath)
    {
        var results = new JsonArray();
        foreach (var method in EnumerateMethods(type, visibility))
        {
            var signature = CreateMethodSignature(method);
            var descriptor = new JsonObject
            {
                ["name"] = method.Name,
                ["signature"] = signature,
                ["declaringType"] = GetTypeDisplayName(method.DeclaringType!),
                ["returnType"] = GetTypeDisplayName(method.ReturnType),
                ["parameterTypes"] = CreateStringArray(method.GetParameters().Select(parameter => GetTypeDisplayName(parameter.ParameterType))),
                ["visibility"] = method.IsPublic ? "public" : "non_public"
            };

            if (allowedInvocations != null)
            {
                var invocationKey = CreateInvocationKey(targetPath, signature);
                descriptor["invokable"] = allowedInvocations.Contains(invocationKey) ||
                                          (string.IsNullOrWhiteSpace(targetPath) && allowedInvocations.Contains(signature));
            }

            results.Add(descriptor);
        }

        return results;
    }

    private static JsonObject CreateSnapshot(
        object? value,
        Type? declaredType,
        string? path,
        int depthRemaining,
        ReflectionSnapshotState state,
        bool allowExpansion,
        bool isRoot)
    {
        var runtimeType = value?.GetType();
        var effectiveType = runtimeType ?? declaredType;
        var snapshot = new JsonObject
        {
            ["path"] = path,
            ["declaredType"] = declaredType == null ? null : GetTypeDisplayName(declaredType),
            ["runtimeType"] = runtimeType == null ? null : GetTypeDisplayName(runtimeType),
            ["kind"] = GetValueKind(effectiveType, value),
            ["isNull"] = value == null,
            ["preview"] = CreatePreview(value, effectiveType),
            ["expandable"] = false
        };

        if (value == null || effectiveType == null)
        {
            return snapshot;
        }

        if (!isRoot && !state.IsTypeAllowed(effectiveType))
        {
            snapshot["opaque"] = true;
            return snapshot;
        }

        if (IsSimpleType(effectiveType))
        {
            return snapshot;
        }

        if (!state.TryEnter(value, path))
        {
            snapshot["cycleDetected"] = true;
            snapshot["expandable"] = false;
            return snapshot;
        }

        try
        {
            if (TryDescribeDictionary(value, effectiveType, path, depthRemaining, state, out var dictionaryItems))
            {
                snapshot["expandable"] = true;
                snapshot["items"] = dictionaryItems;
                return snapshot;
            }

            if (TryDescribeCollection(value, effectiveType, path, depthRemaining, state, out var collectionItems, out var truncated))
            {
                snapshot["expandable"] = true;
                snapshot["items"] = collectionItems;
                snapshot["truncated"] = truncated;
                return snapshot;
            }

            var members = new JsonArray();
            foreach (var member in EnumerateMembers(effectiveType, state.MemberVisibility))
            {
                var childPath = AppendMemberPath(path, member.Name);
                var descriptor = CreateMemberDescriptor(value, member, childPath, depthRemaining, state);
                members.Add(descriptor);
            }

            snapshot["expandable"] = members.Count > 0;
            snapshot["members"] = members;
            snapshot["methods"] = DescribeMethods(effectiveType, state.MemberVisibility, state.Root.AllowedInvokableMethods, path);
            return snapshot;
        }
        finally
        {
            state.Exit(value);
        }
    }

    private static JsonObject CreateMemberDescriptor(
        object instance,
        MemberInfo member,
        string childPath,
        int depthRemaining,
        ReflectionSnapshotState state)
    {
        object? childValue = null;
        Type? childType = null;
        var readable = false;
        var writable = false;
        string? error = null;

        try
        {
            switch (member)
            {
                case PropertyInfo property:
                    readable = property.CanRead && property.GetMethod != null;
                    writable = property.CanWrite && property.SetMethod != null;
                    childType = property.PropertyType;
                    if (readable)
                    {
                        childValue = property.GetValue(instance);
                    }

                    break;
                case FieldInfo field:
                    readable = true;
                    writable = !field.IsInitOnly;
                    childType = field.FieldType;
                    childValue = field.GetValue(instance);
                    break;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var descriptor = new JsonObject
        {
            ["name"] = member.Name,
            ["path"] = childPath,
            ["memberType"] = member.MemberType == MemberTypes.Field ? "field" : "property",
            ["declaringType"] = GetTypeDisplayName(member.DeclaringType!),
            ["readable"] = readable,
            ["writable"] = writable,
            ["visibility"] = IsPublic(member) ? "public" : "non_public",
            ["allowedWrite"] = state.Root.AllowedWritableMembers.Contains(childPath),
            ["error"] = error
        };

        if (childType != null)
        {
            descriptor["type"] = GetTypeDisplayName(childType);
        }

        if (readable && error == null)
        {
            descriptor["value"] = CreateSnapshot(
                childValue,
                childType,
                childPath,
                depthRemaining > 0 ? depthRemaining - 1 : 0,
                state,
                allowExpansion: depthRemaining > 0,
                isRoot: false);
        }

        return descriptor;
    }

    private static bool TryDescribeDictionary(
        object value,
        Type runtimeType,
        string? path,
        int depthRemaining,
        ReflectionSnapshotState state,
        out JsonArray items)
    {
        items = new JsonArray();
        if (value is IDictionary dictionary)
        {
            var count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string stringKey)
                {
                    continue;
                }

                if (count++ >= state.MaxItemsPerCollection)
                {
                    break;
                }

                var itemPath = AppendDictionaryPath(path, stringKey);
                items.Add(new JsonObject
                {
                    ["key"] = stringKey,
                    ["path"] = itemPath,
                    ["value"] = CreateSnapshot(
                        entry.Value,
                        entry.Value?.GetType(),
                        itemPath,
                        depthRemaining > 0 ? depthRemaining - 1 : 0,
                        state,
                        allowExpansion: depthRemaining > 0,
                        isRoot: false)
                });
            }

            return true;
        }

        var dictionaryInterface = runtimeType
            .GetInterfaces()
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
                candidate.GetGenericArguments()[0] == typeof(string));

        if (dictionaryInterface == null)
        {
            return false;
        }

        var enumerator = ((IEnumerable)value).GetEnumerator();
        var index = 0;
        while (enumerator.MoveNext() && index < state.MaxItemsPerCollection)
        {
            var entry = enumerator.Current;
            if (entry == null)
            {
                continue;
            }

            var keyProperty = entry.GetType().GetProperty("Key");
            var valueProperty = entry.GetType().GetProperty("Value");
            var key = keyProperty?.GetValue(entry) as string;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var entryValue = valueProperty?.GetValue(entry);
            var itemPath = AppendDictionaryPath(path, key);
            items.Add(new JsonObject
            {
                ["key"] = key,
                ["path"] = itemPath,
                ["value"] = CreateSnapshot(
                    entryValue,
                    entryValue?.GetType(),
                    itemPath,
                    depthRemaining > 0 ? depthRemaining - 1 : 0,
                    state,
                    allowExpansion: depthRemaining > 0,
                    isRoot: false)
            });
            index++;
        }

        return true;
    }

    private static bool TryDescribeCollection(
        object value,
        Type runtimeType,
        string? path,
        int depthRemaining,
        ReflectionSnapshotState state,
        out JsonArray items,
        out bool truncated)
    {
        items = new JsonArray();
        truncated = false;

        if (value is string || value is not IEnumerable enumerable)
        {
            return false;
        }

        var index = 0;
        foreach (var item in enumerable)
        {
            if (index >= state.MaxItemsPerCollection)
            {
                truncated = true;
                break;
            }

            var itemPath = AppendIndexPath(path, index);
            items.Add(new JsonObject
            {
                ["index"] = index,
                ["path"] = itemPath,
                ["value"] = CreateSnapshot(
                    item,
                    item?.GetType(),
                    itemPath,
                    depthRemaining > 0 ? depthRemaining - 1 : 0,
                    state,
                    allowExpansion: depthRemaining > 0,
                    isRoot: false)
            });
            index++;
        }

        return runtimeType != typeof(string);
    }

    private static ReflectionResolvedValue ResolveTarget(object root, IReadOnlyList<ReflectionPathSegment> segments, ReflectionMemberVisibility visibility)
    {
        object? current = root;
        var declaredType = root.GetType();

        foreach (var segment in segments)
        {
            if (current == null)
            {
                throw new InvalidOperationException("Path resolved through a null value.");
            }

            switch (segment.Kind)
            {
                case ReflectionPathSegmentKind.Member:
                    var member = FindMember(current.GetType(), segment.Value!, visibility)
                                 ?? throw new InvalidOperationException($"Member '{segment.Value}' was not found.");
                    switch (member)
                    {
                        case PropertyInfo property when property.CanRead && property.GetMethod != null:
                            current = property.GetValue(current);
                            declaredType = property.PropertyType;
                            break;
                        case FieldInfo field:
                            current = field.GetValue(current);
                            declaredType = field.FieldType;
                            break;
                        case PropertyInfo property:
                            throw new InvalidOperationException($"Property '{segment.Value}' is not readable.");
                        default:
                            throw new InvalidOperationException($"Member '{segment.Value}' is not readable.");
                    }

                    break;
                case ReflectionPathSegmentKind.Index:
                    current = ResolveIndex(current, segment.Index!.Value, out declaredType);
                    break;
                case ReflectionPathSegmentKind.DictionaryKey:
                    current = ResolveDictionaryKey(current, segment.Value!, out declaredType);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported path segment.");
            }
        }

        return new ReflectionResolvedValue(current, declaredType);
    }

    private static object? ResolveIndex(object current, int index, out Type? declaredType)
    {
        switch (current)
        {
            case Array array:
                if (index < 0 || index >= array.Length)
                {
                    throw new InvalidOperationException($"Index {index} is out of range.");
                }

                declaredType = array.GetType().GetElementType();
                return array.GetValue(index);
            case IList list:
                if (index < 0 || index >= list.Count)
                {
                    throw new InvalidOperationException($"Index {index} is out of range.");
                }

                declaredType = TryGetListElementType(list.GetType());
                return list[index];
            default:
                throw new InvalidOperationException("Indexed path segments require an array or list target.");
        }
    }

    private static object? ResolveDictionaryKey(object current, string key, out Type? declaredType)
    {
        if (current is IDictionary dictionary)
        {
            declaredType = TryGetDictionaryValueType(current.GetType());
            return dictionary.Contains(key)
                ? dictionary[key]
                : throw new InvalidOperationException($"Dictionary key '{key}' was not found.");
        }

        var indexer = current.GetType().GetProperty(
            "Item",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            returnType: null,
            types: new[] { typeof(string) },
            modifiers: null);
        var containsKey = current.GetType().GetMethod("ContainsKey", new[] { typeof(string) });
        if (indexer == null || containsKey == null)
        {
            throw new InvalidOperationException("Dictionary-key path segments require a string-keyed dictionary target.");
        }

        declaredType = TryGetDictionaryValueType(current.GetType());
        var found = (bool)(containsKey.Invoke(current, new object?[] { key }) ?? false);
        if (!found)
        {
            throw new InvalidOperationException($"Dictionary key '{key}' was not found.");
        }

        return indexer.GetValue(current, new object?[] { key });
    }

    private static MemberInfo? FindMember(Type type, string name, ReflectionMemberVisibility visibility)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var flags = BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public;
            if (visibility == ReflectionMemberVisibility.PublicAndNonPublic)
            {
                flags |= BindingFlags.NonPublic;
            }

            var property = current
                .GetProperties(flags)
                .FirstOrDefault(candidate => candidate.GetIndexParameters().Length == 0 &&
                                             string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (property != null)
            {
                return property;
            }

            var field = current
                .GetFields(flags)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static IReadOnlyList<MemberInfo> EnumerateMembers(Type type, ReflectionMemberVisibility visibility)
    {
        var results = new List<MemberInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type; current != null; current = current.BaseType)
        {
            var flags = BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public;
            if (visibility == ReflectionMemberVisibility.PublicAndNonPublic)
            {
                flags |= BindingFlags.NonPublic;
            }

            foreach (var property in current.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (seen.Add($"property:{property.Name}"))
                {
                    results.Add(property);
                }
            }

            foreach (var field in current.GetFields(flags))
            {
                if (seen.Add($"field:{field.Name}"))
                {
                    results.Add(field);
                }
            }
        }

        return results
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<MethodInfo> EnumerateMethods(Type type, ReflectionMemberVisibility visibility)
    {
        var results = new List<MethodInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type; current != null; current = current.BaseType)
        {
            var flags = BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public;
            if (visibility == ReflectionMemberVisibility.PublicAndNonPublic)
            {
                flags |= BindingFlags.NonPublic;
            }

            foreach (var method in current.GetMethods(flags))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                var signature = CreateMethodSignature(method);
                if (seen.Add(signature))
                {
                    results.Add(method);
                }
            }
        }

        return results
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => method.GetParameters().Length)
            .ToList();
    }

    private static MethodInfo ResolveMethod(
        Type type,
        string methodName,
        IReadOnlyList<string> parameterTypeNames,
        ReflectionMemberVisibility visibility)
    {
        var candidates = EnumerateMethods(type, visibility)
            .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"Method '{methodName}' was not found.");
        }

        if (parameterTypeNames.Count > 0)
        {
            var resolvedParameterTypes = parameterTypeNames
                .Select(parameterTypeName => ResolveType(parameterTypeName, assemblyName: null)
                                             ?? throw new InvalidOperationException($"Parameter type '{parameterTypeName}' could not be resolved."))
                .ToArray();

            var matched = candidates.Where(candidate => ParametersMatch(candidate.GetParameters(), resolvedParameterTypes)).ToList();
            return matched.Count switch
            {
                0 => throw new InvalidOperationException($"No overload of '{methodName}' matched the supplied parameter types."),
                > 1 => throw new InvalidOperationException($"Multiple overloads of '{methodName}' matched the supplied parameter types."),
                _ => matched[0]
            };
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            _ => throw new InvalidOperationException($"Method '{methodName}' is overloaded. Supply parameterTypesJson to disambiguate.")
        };
    }

    private static bool ParametersMatch(IReadOnlyList<ParameterInfo> parameters, IReadOnlyList<Type> parameterTypes)
    {
        if (parameters.Count != parameterTypes.Count)
        {
            return false;
        }

        for (var index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].ParameterType != parameterTypes[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateMethodSignature(MethodInfo method)
    {
        var parameterTypes = method
            .GetParameters()
            .Select(parameter => GetTypeDisplayName(parameter.ParameterType));

        return $"{method.Name}({string.Join(",", parameterTypes)})";
    }

    private static string CreateInvocationKey(string? targetPath, string signature)
        => string.IsNullOrWhiteSpace(targetPath)
            ? signature
            : $"{targetPath}#{signature}";

    private static Type? ResolveType(string typeName, string? assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var assemblyQualifiedName = $"{typeName}, {assemblyName}";
            var resolved = Type.GetType(assemblyQualifiedName, throwOnError: false);
            if (resolved != null)
            {
                return resolved;
            }
        }

        var direct = Type.GetType(typeName, throwOnError: false);
        if (direct != null)
        {
            return direct;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                assemblyTypes = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            }

            var resolved = assembly.GetType(typeName, throwOnError: false)
                           ?? assemblyTypes.FirstOrDefault(candidate =>
                               string.Equals(candidate.FullName, typeName, StringComparison.Ordinal) ||
                               string.Equals(candidate.Name, typeName, StringComparison.Ordinal));
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string GetTypeDisplayName(Type type)
        => type.FullName ?? type.Name;

    private static string GetValueKind(Type? type, object? value)
    {
        if (type == null)
        {
            return value == null ? "null" : "unknown";
        }

        if (type == typeof(void))
        {
            return "void";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type.IsEnum)
        {
            return "enum";
        }

        if (IsNumericType(type))
        {
            return "number";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (typeof(IDictionary).IsAssignableFrom(type) || ImplementsGenericDictionary(type))
        {
            return "dictionary";
        }

        if (type.IsArray)
        {
            return "array";
        }

        if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return "collection";
        }

        return "object";
    }

    private static string? CreatePreview(object? value, Type? type)
    {
        if (value == null)
        {
            return "null";
        }

        if (type == null)
        {
            return value.ToString();
        }

        if (type == typeof(string))
        {
            var stringValue = (string)value;
            return stringValue.Length > 120
                ? stringValue[..120] + "..."
                : stringValue;
        }

        if (type.IsEnum || IsNumericType(type) || type == typeof(bool) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is ICollection collection)
        {
            return $"{GetTypeDisplayName(type)} ({collection.Count} item(s))";
        }

        if (value is IEnumerable && type != typeof(string))
        {
            return GetTypeDisplayName(type);
        }

        return GetTypeDisplayName(type);
    }

    private static JsonArray CreateStringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static bool IsSimpleType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsPrimitive ||
               underlyingType.IsEnum ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(Guid) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(TimeSpan);
    }

    private static bool IsNumericType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(byte) ||
               underlyingType == typeof(sbyte) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(ushort) ||
               underlyingType == typeof(int) ||
               underlyingType == typeof(uint) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(ulong) ||
               underlyingType == typeof(float) ||
               underlyingType == typeof(double) ||
               underlyingType == typeof(decimal);
    }

    private static bool IsPublic(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true,
            FieldInfo field => field.IsPublic,
            _ => false
        };
    }

    private static bool ImplementsGenericDictionary(Type type)
        => type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
            candidate.GetGenericArguments()[0] == typeof(string));

    private static Type? TryGetListElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var listInterface = type
            .GetInterfaces()
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IList<>));

        return listInterface?.GetGenericArguments()[0];
    }

    private static Type? TryGetDictionaryValueType(Type type)
    {
        var dictionaryInterface = type
            .GetInterfaces()
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) &&
                candidate.GetGenericArguments()[0] == typeof(string));

        return dictionaryInterface?.GetGenericArguments()[1];
    }

    private static object? ConvertValue(JsonNode? node, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (node == null)
        {
            if (underlyingType != null || !targetType.IsValueType)
            {
                return null;
            }

            throw new InvalidOperationException($"Null cannot be assigned to '{GetTypeDisplayName(targetType)}'.");
        }

        if (underlyingType != null)
        {
            return ConvertValue(node, underlyingType);
        }

        if (targetType == typeof(string))
        {
            return node is JsonValue stringValue && stringValue.TryGetValue<string>(out var typedString)
                ? typedString
                : node.ToJsonString();
        }

        if (targetType == typeof(JsonNode))
        {
            return node.DeepClone();
        }

        if (targetType.IsEnum)
        {
            if (node is JsonValue enumValue && enumValue.TryGetValue<string>(out var enumString))
            {
                return Enum.Parse(targetType, enumString, ignoreCase: true);
            }

            if (node is JsonValue numericEnumValue && numericEnumValue.TryGetValue<int>(out var enumInt))
            {
                return Enum.ToObject(targetType, enumInt);
            }
        }

        try
        {
            return node.Deserialize(targetType, JsonSerializerOptions.Default);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not convert the supplied JSON value to '{GetTypeDisplayName(targetType)}': {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<ReflectionPathSegment> ParsePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<ReflectionPathSegment>();
        }

        var segments = new List<ReflectionPathSegment>();
        var span = path.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            if (span[index] == '.')
            {
                index++;
                continue;
            }

            if (span[index] == '[')
            {
                index++;
                if (index < span.Length && span[index] == '"')
                {
                    index++;
                    var keyStart = index;
                    while (index < span.Length && span[index] != '"')
                    {
                        if (span[index] == '\\' && index + 1 < span.Length)
                        {
                            index += 2;
                            continue;
                        }

                        index++;
                    }

                    if (index >= span.Length)
                    {
                        throw new InvalidOperationException($"Path '{path}' contains an unterminated dictionary key segment.");
                    }

                    var rawKey = path.Substring(keyStart, index - keyStart);
                    index++;
                    if (index >= span.Length || span[index] != ']')
                    {
                        throw new InvalidOperationException($"Path '{path}' contains an invalid dictionary key segment.");
                    }

                    segments.Add(new ReflectionPathSegment(ReflectionPathSegmentKind.DictionaryKey, UnescapePathString(rawKey), null));
                    index++;
                    continue;
                }

                var numberStart = index;
                while (index < span.Length && char.IsDigit(span[index]))
                {
                    index++;
                }

                if (numberStart == index || index >= span.Length || span[index] != ']')
                {
                    throw new InvalidOperationException($"Path '{path}' contains an invalid index segment.");
                }

                var numberText = path.Substring(numberStart, index - numberStart);
                segments.Add(new ReflectionPathSegment(ReflectionPathSegmentKind.Index, null, int.Parse(numberText, System.Globalization.CultureInfo.InvariantCulture)));
                index++;
                continue;
            }

            var start = index;
            while (index < span.Length && span[index] != '.' && span[index] != '[')
            {
                index++;
            }

            var memberName = path.Substring(start, index - start);
            if (string.IsNullOrWhiteSpace(memberName))
            {
                throw new InvalidOperationException($"Path '{path}' contains an empty member segment.");
            }

            segments.Add(new ReflectionPathSegment(ReflectionPathSegmentKind.Member, memberName, null));
        }

        return segments;
    }

    private static string UnescapePathString(string value)
        => value.Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static JsonNode? ParseJsonArgument(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }

    private static IReadOnlyList<string> ParseStringArrayArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        if (ParseJsonArgument(value) is not JsonArray array)
        {
            throw new InvalidOperationException("Expected a JSON array of strings.");
        }

        return array
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray();
    }

    private static IReadOnlyList<JsonNode?> ParseJsonArrayArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<JsonNode?>();
        }

        if (ParseJsonArgument(value) is not JsonArray array)
        {
            throw new InvalidOperationException("Expected a JSON array of argument values.");
        }

        return array.ToArray();
    }

    internal static string GetRequiredString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        var value = GetString(arguments, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Argument '{key}' is required.");
        }

        return value;
    }

    internal static string? GetString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int defaultValue, int minimum, int maximum)
    {
        var rawValue = GetString(arguments, key);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out var parsed))
        {
            throw new InvalidOperationException($"Argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsed, minimum, maximum);
    }

    private static string AppendMemberPath(string? basePath, string memberName)
        => string.IsNullOrWhiteSpace(basePath)
            ? memberName
            : $"{basePath}.{memberName}";

    private static string AppendIndexPath(string? basePath, int index)
        => string.IsNullOrWhiteSpace(basePath)
            ? $"[{index}]"
            : $"{basePath}[{index}]";

    private static string AppendDictionaryPath(string? basePath, string key)
        => string.IsNullOrWhiteSpace(basePath)
            ? $"[\"{key}\"]"
            : $"{basePath}[\"{key}\"]";

    private sealed class ReflectionSnapshotState
    {
        private readonly HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        private readonly ReflectionToolsOptions options;

        public ReflectionSnapshotState(ReflectionToolsOptions options, ReflectionRootRegistration root, int maxItemsPerCollection)
        {
            this.options = options;
            Root = root;
            MaxItemsPerCollection = maxItemsPerCollection;
        }

        public ReflectionRootRegistration Root { get; }

        public int MaxItemsPerCollection { get; }

        public ReflectionMemberVisibility MemberVisibility => Root.MemberVisibility ?? options.DefaultMemberVisibility;

        public bool TryEnter(object value, string? path)
        {
            if (value is string || value.GetType().IsValueType)
            {
                return true;
            }

            return visited.Add(value);
        }

        public void Exit(object value)
        {
            if (value is string || value.GetType().IsValueType)
            {
                return;
            }

            _ = visited.Remove(value);
        }

        public bool IsTypeAllowed(Type type)
        {
            if (options.AllowedAssemblies.Count == 0 && options.AllowedNamespacePrefixes.Count == 0)
            {
                return true;
            }

            var assemblyName = type.Assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName) && options.AllowedAssemblies.Contains(assemblyName))
            {
                return true;
            }

            var namespaceName = type.Namespace ?? string.Empty;
            return options.AllowedNamespacePrefixes.Any(prefix =>
                namespaceName.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    internal sealed record ReflectionResolvedValue(object? Value, Type? DeclaredType);

    internal sealed record ReflectionPathSegment(ReflectionPathSegmentKind Kind, string? Value, int? Index);

    internal enum ReflectionPathSegmentKind
    {
        Member = 0,
        Index = 1,
        DictionaryKey = 2
    }
}
