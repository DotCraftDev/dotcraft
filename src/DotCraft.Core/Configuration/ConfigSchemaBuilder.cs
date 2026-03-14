using System.Reflection;

namespace DotCraft.Configuration;

/// <summary>
/// Builds a list of <see cref="ConfigSchemaSection"/> by reflecting over types annotated with
/// <see cref="ConfigSectionAttribute"/>. This is the single source of truth for both the
/// Dashboard config UI schema and the sensitive-field masking list.
/// </summary>
public static class ConfigSchemaBuilder
{
    /// <summary>
    /// Builds the complete schema from a set of config types.
    /// Each type must be annotated with <see cref="ConfigSectionAttribute"/>.
    /// Types without the attribute are silently skipped.
    /// </summary>
    public static List<ConfigSchemaSection> BuildAll(IEnumerable<Type> configTypes)
    {
        return configTypes
            .Select(BuildSection)
            .Where(s => s != null)
            .OrderBy(s => s!.Order)
            .ToList()!;
    }

    /// <summary>
    /// Derives the sensitive-field path list from a schema.
    /// Used to replace the hardcoded SensitivePaths array in DashBoardMiddleware.
    /// </summary>
    public static string[][] BuildSensitivePaths(IEnumerable<ConfigSchemaSection> schema)
    {
        var paths = new List<string[]>();
        foreach (var section in schema)
        {
            foreach (var field in section.Fields.Where(f => f.Sensitive))
            {
                if (section.RootKey != null)
                {
                    paths.Add([section.RootKey, field.Key]);
                }
                else if (section.Path is { Length: > 0 })
                {
                    var path = new string[section.Path.Length + 1];
                    section.Path.CopyTo(path, 0);
                    path[section.Path.Length] = field.Key;
                    paths.Add(path);
                }
                else
                {
                    paths.Add([field.Key]);
                }
            }
        }
        return paths.ToArray();
    }

    private static ConfigSchemaSection? BuildSection(Type type)
    {
        var sectionAttr = type.GetCustomAttribute<ConfigSectionAttribute>();
        if (sectionAttr == null) return null;

        // RootKey sections (e.g. McpServers) are rendered as a single JSON textarea
        if (sectionAttr.RootKey != null)
        {
            return new ConfigSchemaSection
            {
                Section = sectionAttr.DisplayName ?? sectionAttr.RootKey,
                Order = sectionAttr.Order,
                RootKey = sectionAttr.RootKey,
                Fields =
                [
                    new ConfigSchemaField
                    {
                        Key = sectionAttr.RootKey,
                        Type = "json",
                        Hint = $"JSON array of {sectionAttr.RootKey} configs"
                    }
                ]
            };
        }

        var instance = TryCreateInstance(type);
        var fields = new List<ConfigSchemaField>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var fieldAttr = prop.GetCustomAttribute<ConfigFieldAttribute>();

            if (fieldAttr?.Ignore == true) continue;

            // Skip nested sub-section properties (they have their own ConfigSectionAttribute)
            if (prop.PropertyType.GetCustomAttribute<ConfigSectionAttribute>() != null) continue;

            // Skip JsonExtensionData / other infra properties
            if (prop.Name == "ExtensionData") continue;

            fields.Add(BuildField(prop, fieldAttr, instance));
        }

        var path = BuildPath(sectionAttr);
        return new ConfigSchemaSection
        {
            Section = sectionAttr.DisplayName ?? sectionAttr.Key,
            Order = sectionAttr.Order,
            Path = path,
            Fields = fields
        };
    }

    private static ConfigSchemaField BuildField(PropertyInfo prop, ConfigFieldAttribute? attr, object? instance)
    {
        var inferredType = InferFieldType(prop.PropertyType, attr);
        var defaultValue = instance != null ? NormalizeDefault(prop.GetValue(instance), prop.PropertyType) : null;
        // Explicit options on attribute take priority; otherwise infer from enum type
        var options = attr?.Options ?? InferOptions(prop.PropertyType);

        int? min = attr?.Min != null && attr.Min != int.MinValue ? attr.Min : null;
        int? max = attr?.Max != null && attr.Max != int.MaxValue ? attr.Max : null;

        return new ConfigSchemaField
        {
            Key = prop.Name,
            Type = inferredType,
            Sensitive = attr?.Sensitive ?? false,
            Options = options,
            Min = min,
            Max = max,
            Hint = attr?.Hint,
            DefaultValue = defaultValue,
        };
    }

    private static string InferFieldType(Type type, ConfigFieldAttribute? attr)
    {
        if (attr?.FieldType != null) return attr.FieldType;
        if (attr?.Sensitive == true) return "password";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float)) return "number";
        if (type == typeof(string)) return "text";
        if (type.IsEnum) return "select";
        if (IsCollectionOrDictionary(type)) return "json";
        return "text";
    }

    private static string[]? InferOptions(Type type)
    {
        if (!type.IsEnum) return null;
        return Enum.GetNames(type);
    }

    private static bool IsCollectionOrDictionary(Type type)
    {
        if (type.IsArray) return true;
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def == typeof(List<>) || def == typeof(Dictionary<,>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>);
    }

    private static string[]? BuildPath(ConfigSectionAttribute attr)
    {
        if (string.IsNullOrEmpty(attr.Key)) return null;

        // Support dot-separated keys like "Tools.File" -> ["Tools", "File"]
        var parts = attr.Key.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts : null;
    }

    private static object? NormalizeDefault(object? value, Type type)
    {
        if (value == null) return null;

        // Empty string, empty list, false bool, zero int -> keep for UI display consistency
        // (the UI uses defaultValue to show placeholder text)
        if (type == typeof(string) && string.IsNullOrEmpty((string)value)) return null;

        if (IsCollectionOrDictionary(type))
        {
            // Empty collections have no meaningful default to display
            var countProp = value.GetType().GetProperty("Count");
            if (countProp?.GetValue(value) is int count && count == 0) return null;
        }

        return value;
    }

    private static object? TryCreateInstance(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }
}
