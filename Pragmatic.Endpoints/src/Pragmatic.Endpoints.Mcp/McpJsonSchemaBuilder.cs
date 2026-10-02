using System.Text.Json;
using Pragmatic.Endpoints.OpenApi;

namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Builds the MCP tool input JSON Schema from the compile-time manifest: route/query
///     parameters and request-body properties become top-level schema properties.
///     Zero reflection — everything comes from the manifest.
/// </summary>
public static class McpJsonSchemaBuilder
{
    /// <summary>Builds the input schema JSON for one endpoint.</summary>
    public static string Build(ManifestEndpoint endpoint)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");

            var required = new List<string>();

            foreach (var parameter in endpoint.Parameters ?? [])
            {
                if (parameter.Name is null) continue;
                WriteProperty(writer, parameter.Name, parameter.Type, description: $"{parameter.In} parameter");
                if (parameter.IsRequired && parameter.In is "path")
                    required.Add(parameter.Name);
            }

            foreach (var property in endpoint.RequestBody?.Properties ?? [])
            {
                if (property.Name is null) continue;
                WriteProperty(writer, CamelCase(property.Name), property.Type, maxLength: property.MaxLength);
                if (property.IsRequired)
                    required.Add(CamelCase(property.Name));
            }

            writer.WriteEndObject(); // properties

            if (required.Count > 0)
            {
                writer.WriteStartArray("required");
                foreach (var name in required.Distinct(StringComparer.Ordinal))
                    writer.WriteStringValue(name);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteProperty(
        Utf8JsonWriter writer, string name, string? clrType, string? description = null, int? maxLength = null)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", MapJsonType(clrType));
        if (MapFormat(clrType) is { } format)
            writer.WriteString("format", format);
        if (description is not null)
            writer.WriteString("description", description);
        if (maxLength is { } max)
            writer.WriteNumber("maxLength", max);
        writer.WriteEndObject();
    }

    private static string MapJsonType(string? clrType)
    {
        var simple = Simple(clrType);
        return simple switch
        {
            "int" or "int32" or "int64" or "long" or "short" or "byte" => "integer",
            "bool" or "boolean" => "boolean",
            "decimal" or "double" or "float" or "single" => "number",
            _ => "string"
        };
    }

    private static string? MapFormat(string? clrType)
        => Simple(clrType) switch
        {
            "guid" => "uuid",
            "datetimeoffset" or "datetime" => "date-time",
            "dateonly" => "date",
            _ => null
        };

    private static string Simple(string? clrType)
    {
        if (clrType is null) return "string";
        var s = clrType.Replace("global::", "").TrimEnd('?');
        var lastDot = s.LastIndexOf('.');
        return (lastDot >= 0 ? s[(lastDot + 1)..] : s).ToLowerInvariant();
    }

    internal static string CamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
