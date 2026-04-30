namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    internal static JsonArray CreateBindablePropertiesArray(BindableObject bindable)
    {
        var properties = new JsonArray();
        foreach (var descriptor in GetBindablePropertyDescriptors(bindable.GetType()))
        {
            properties.Add(CreateBindablePropertyMetadata(bindable, descriptor));
        }

        return properties;
    }

    internal static BindablePropertyDescriptor? ResolveBindableProperty(
        BindableObject bindable,
        string propertyName,
        string? declaringTypeName)
    {
        var normalizedPropertyName = propertyName.Trim();
        var descriptors = GetBindablePropertyDescriptors(bindable.GetType());
        var matches = descriptors
            .Where(descriptor => IsBindablePropertyMatch(descriptor, normalizedPropertyName))
            .Where(descriptor => string.IsNullOrWhiteSpace(declaringTypeName) || IsTypeNameMatch(descriptor.DeclaringType, declaringTypeName!))
            .ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        return matches.FirstOrDefault(descriptor => string.Equals(descriptor.BindableProperty.PropertyName, normalizedPropertyName, StringComparison.Ordinal))
            ?? matches.FirstOrDefault();
    }

    internal static bool IsBindablePropertyMatch(BindablePropertyDescriptor descriptor, string propertyName)
    {
        var memberNameWithoutSuffix = descriptor.MemberName.EndsWith("Property", StringComparison.Ordinal)
            ? descriptor.MemberName[..^"Property".Length]
            : descriptor.MemberName;

        return string.Equals(descriptor.BindableProperty.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(descriptor.MemberName, propertyName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberNameWithoutSuffix, propertyName, StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<BindablePropertyDescriptor> GetBindablePropertyDescriptors(Type type)
    {
        var descriptors = new List<BindablePropertyDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!typeof(BindableProperty).IsAssignableFrom(field.FieldType) ||
                    field.GetValue(null) is not BindableProperty bindableProperty)
                {
                    continue;
                }

                AddDescriptor(descriptors, seen, bindableProperty, field.Name, currentType);
            }

            foreach (var property in currentType.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!typeof(BindableProperty).IsAssignableFrom(property.PropertyType) ||
                    property.GetIndexParameters().Length > 0 ||
                    property.GetValue(null) is not BindableProperty bindableProperty)
                {
                    continue;
                }

                AddDescriptor(descriptors, seen, bindableProperty, property.Name, currentType);
            }
        }

        return descriptors;
    }

    internal static void AddDescriptor(
        List<BindablePropertyDescriptor> descriptors,
        HashSet<string> seen,
        BindableProperty bindableProperty,
        string memberName,
        Type declaringType)
    {
        var key = $"{declaringType.FullName}|{memberName}";
        if (!seen.Add(key))
        {
            return;
        }

        descriptors.Add(new BindablePropertyDescriptor(bindableProperty, memberName, declaringType));
    }

    internal static JsonObject CreateBindablePropertyMetadata(BindableObject bindable, BindablePropertyDescriptor descriptor)
    {
        return new JsonObject
        {
            ["name"] = descriptor.BindableProperty.PropertyName,
            ["memberName"] = descriptor.MemberName,
            ["declaringType"] = CreateTypeMetadata(descriptor.DeclaringType),
            ["valueType"] = CreateTypeMetadata(descriptor.BindableProperty.ReturnType),
            ["defaultBindingMode"] = descriptor.BindableProperty.DefaultBindingMode.ToString(),
            ["isSet"] = IsBindablePropertySet(bindable, descriptor.BindableProperty),
            ["hasBinding"] = GetBinding(bindable, descriptor.BindableProperty) != null
        };
    }

    internal static bool IsBindablePropertySet(BindableObject bindable, BindableProperty bindableProperty)
    {
        try
        {
            return bindable.IsSet(bindableProperty);
        }
        catch
        {
            return false;
        }
    }

    internal static JsonObject? CreateBindingInfo(BindableObject bindable, BindableProperty bindableProperty)
    {
        var binding = GetBinding(bindable, bindableProperty);
        if (binding == null)
        {
            return null;
        }

        var json = new JsonObject
        {
            ["type"] = GetTypeDisplayName(binding.GetType())
        };

        foreach (var propertyName in new[] { "Mode", "Path", "StringFormat", "FallbackValue", "TargetNullValue", "Source" })
        {
            if (!TryReadPublicProperty(binding, propertyName, out var value, out var propertyType))
            {
                continue;
            }

            json[ToCamelCase(propertyName)] = CreateValueSnapshot(value, propertyType, depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
        }

        return json;
    }

    internal static object? GetBinding(BindableObject bindable, BindableProperty bindableProperty)
    {
        try
        {
            var method = typeof(BindableObject).GetMethod(
                "GetBinding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(BindableProperty) },
                modifiers: null);

            return method?.Invoke(bindable, new object[] { bindableProperty });
        }
        catch
        {
            return null;
        }
    }

    internal static bool RemoveBinding(BindableObject bindable, BindableProperty bindableProperty)
    {
        try
        {
            var method = typeof(BindableObject).GetMethod(
                "RemoveBinding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(BindableProperty) },
                modifiers: null);

            method?.Invoke(bindable, new object[] { bindableProperty });
            return method != null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryReadPublicProperty(object target, string propertyName, out object? value, out Type? propertyType)
    {
        value = null;
        propertyType = null;

        var property = ResolvePublicInstanceProperty(target.GetType(), propertyName);
        if (property == null || property.GetIndexParameters().Length > 0)
        {
            return false;
        }

        try
        {
            value = property.GetValue(target);
            propertyType = property.PropertyType;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TrySetPublicPropertyFromJson(object target, string propertyName, string valueJson, out object? updatedValue, out string? error)
    {
        updatedValue = null;
        error = null;

        var property = ResolvePublicInstanceProperty(target.GetType(), propertyName);
        if (property == null)
        {
            error = $"The property '{propertyName}' was not found on '{GetTypeDisplayName(target.GetType())}'.";
            return false;
        }

        if (!HasPublicSetter(property))
        {
            error = $"The property '{property.Name}' is not publicly writable.";
            return false;
        }

        object? convertedValue;
        try
        {
            convertedValue = ConvertJsonArgument(valueJson, property.PropertyType);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        try
        {
            property.SetValue(target, convertedValue);
            updatedValue = property.GetValue(target);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal static bool TryResolveCommand(
        object target,
        string commandName,
        out ICommand? command,
        out object? commandParameter,
        out string? matchedPropertyName)
    {
        command = null;
        commandParameter = null;
        matchedPropertyName = null;

        var candidates = new[]
        {
            commandName,
            commandName.EndsWith("Command", StringComparison.OrdinalIgnoreCase) ? commandName : $"{commandName}Command"
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var property = ResolvePublicInstanceProperty(target.GetType(), candidate);
            if (property == null || !typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            command = property.GetValue(target) as ICommand;
            matchedPropertyName = property.Name;

            var parameterPropertyName = property.Name.EndsWith("Command", StringComparison.Ordinal)
                ? $"{property.Name[..^"Command".Length]}CommandParameter"
                : $"{property.Name}Parameter";
            if (TryReadPublicProperty(target, parameterPropertyName, out var parameterValue, out _))
            {
                commandParameter = parameterValue;
            }

            return command != null;
        }

        return false;
    }

    internal static bool TryExecuteCommand(
        ICommand command,
        object? parameter,
        bool requireCanExecute,
        out string? error)
    {
        error = null;

        bool canExecute;
        try
        {
            canExecute = command.CanExecute(parameter);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        if (!canExecute && requireCanExecute)
        {
            error = "The command returned false from CanExecute.";
            return false;
        }

        try
        {
            command.Execute(parameter);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
#endif
