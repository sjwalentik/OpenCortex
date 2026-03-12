using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace OpenCortex.McpServer;

public static class OpenCortexToolManifest
{
    private static readonly NullabilityInfoContext NullabilityContext = new();
    private static readonly IReadOnlyDictionary<string, int> PreferredToolOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["list_brains"] = 0,
        ["get_brain"] = 1,
        ["query_brain"] = 2,
        ["get_document"] = 3,
        ["save_document"] = 4,
        ["delete_document"] = 5,
        ["create_document"] = 6,
        ["update_document"] = 7,
        ["reindex_brain"] = 8,
    };

    public static IReadOnlyList<McpToolManifestItem> Build()
    {
        return typeof(OpenCortexTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => PreferredToolOrder.TryGetValue(method.Name, out var order) ? order : int.MaxValue)
            .ThenBy(method => method.MetadataToken)
            .Select(BuildItem)
            .ToList();
    }

    private static McpToolManifestItem BuildItem(MethodInfo method)
    {
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        var parameters = method
            .GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Select(parameter => new McpToolManifestParameterItem(
                parameter.Name ?? string.Empty,
                FormatType(parameter.ParameterType),
                IsOptional(parameter),
                parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty))
            .ToList();

        return new McpToolManifestItem(method.Name, description, parameters);
    }

    private static bool IsOptional(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue)
        {
            return true;
        }

        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return true;
        }

        if (!parameter.ParameterType.IsValueType)
        {
            var nullability = NullabilityContext.Create(parameter);
            return nullability.ReadState == NullabilityState.Nullable;
        }

        return false;
    }

    private static string FormatType(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return FormatType(nullable);
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            return "integer";
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return "number";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type.IsArray)
        {
            return $"array<{FormatType(type.GetElementType() ?? typeof(object))}>";
        }

        if (type.IsGenericType)
        {
            var genericType = type.GetGenericTypeDefinition();
            if (genericType == typeof(Dictionary<,>) || genericType == typeof(IReadOnlyDictionary<,>))
            {
                return "object";
            }

            if (genericType == typeof(List<>) || genericType == typeof(IReadOnlyList<>))
            {
                return $"array<{FormatType(type.GetGenericArguments()[0])}>";
            }
        }

        return type.Name;
    }
}

public sealed record McpToolManifestItem(
    string Name,
    string Description,
    IReadOnlyList<McpToolManifestParameterItem> Parameters);

public sealed record McpToolManifestParameterItem(
    string Name,
    string Type,
    bool Optional,
    string Description);
