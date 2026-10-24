using System.Text;

namespace Pragmatic.Persistence.Documents.Generator;

/// <summary>
///     The JSON half of a document's partial, for Cosmos DB: the object written property by property (the key as <c>id</c>, a string), read back
///     by property name, the key's text, the partition key's path and value. Values as <c>DocJson</c> writes them (numbers, a Guid's text, an
///     instant in UTC ISO 8601, an enum's Int32).
/// </summary>
internal static class DocJsonEmitter
{
    private const string Json = "global::Pragmatic.Persistence.Documents.DocJson";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string Element = "global::System.Text.Json.JsonElement";

    public static void Append(StringBuilder source, DocModel model, string contract)
    {
        var name = model.FullName;
        source.AppendLine();
        if (model.IsEmbedded)
        {
            source.AppendLine($"    static void {contract}.WriteJsonFields({Writer} w, {name} e)");
            source.AppendLine("    {");
            foreach (var field in model.Fields)
                source.AppendLine("        " + WriteField(field, "e." + field.Property.Name) + ";");
            source.AppendLine("    }");
        }
        else
        {
            var key = model.Key!;
            source.AppendLine($"    static void {contract}.WriteJson({Writer} w, {name} e)");
            source.AppendLine("    {");
            source.AppendLine("        w.WriteStartObject();");
            source.AppendLine($"        w.WriteString(\"id\", {IdText(key, "e." + key.Property.Name)});");
            foreach (var field in model.Fields)
            {
                if (field != key)
                    source.AppendLine("        " + WriteField(field, "e." + field.Property.Name) + ";");
            }

            source.AppendLine("        w.WriteEndObject();");
            source.AppendLine("    }");
            source.AppendLine();
            source.AppendLine($"    static string {contract}.Id({name} e) => {IdText(key, "e." + key.Property.Name)};");
            source.AppendLine();
            var partition = model.PartitionKey!;
            source.AppendLine($"    static string {contract}.PartitionKeyPath => {DocTypeEmitter.Literal("/" + DocModel.JsonName(partition))};");
            source.AppendLine();
            source.AppendLine(partition == key
                ? $"    static void {contract}.WritePartitionKey({Writer} w, {name} e) => w.WriteStringValue({IdText(key, "e." + key.Property.Name)});"
                : $"    static void {contract}.WritePartitionKey({Writer} w, {name} e) => {Json}.WriteValue(w, {Value(partition, "e." + partition.Property.Name)});");
        }

        source.AppendLine();
        source.AppendLine($"    static {name} {contract}.ReadJson({Element} document)");
        source.AppendLine("    {");
        source.AppendLine($"        var e = new {name}();");
        source.AppendLine("        foreach (var property in document.EnumerateObject())");
        source.AppendLine("        {");
        source.AppendLine("            var v = property.Value;");
        source.AppendLine("            if (v.ValueKind == global::System.Text.Json.JsonValueKind.Null)");
        source.AppendLine("                continue; // a null leaves the property at its default");
        source.AppendLine("            switch (property.Name)");
        source.AppendLine("            {");
        foreach (var field in model.Fields)
        {
            var read = field.Name == "_id" ? ParseId(field) : ReadField(field);
            source.AppendLine($"                case {DocTypeEmitter.Literal(DocModel.JsonName(field))}:");
            source.AppendLine($"                    e.{field.Property.Name} = {read};");
            source.AppendLine("                    break;");
        }

        source.AppendLine("            }");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        return e;");
        source.AppendLine("    }");
    }

    private static string WriteField(DocField field, string value)
    {
        var name = DocTypeEmitter.Literal(DocModel.JsonName(field));
        switch (field.Kind)
        {
            case DocKind.Embedded:
                return $"{Json}.WriteEmbedded<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(w, {name}, {value})";
            case DocKind.List when field.ElementKind == DocKind.Embedded:
                return $"{Json}.WriteEmbeddedList<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(w, {name}, {value})";
            case DocKind.List:
                return $"{Json}.WriteList<{field.Element!.ToDisplayString(DocModel.Qualified)}>(w, {name}, {value}, {Json}.WriteValue)";
            default:
                return $"{Json}.Write(w, {name}, {Value(field, value)})";
        }
    }

    /// <summary>The value as DocJson takes it: an enum as its Int32 (a nullable one as a nullable Int32).</summary>
    private static string Value(DocField field, string value)
        => field.Kind == DocKind.UInt64 ? $"unchecked((long){value})"
            : field.Kind != DocKind.Enum ? value : field.Nullable ? $"({value} is {{ }} enumValue ? (int?)(int)enumValue : null)" : $"(int){value}";

    private static string ReadField(DocField field) => field.Kind switch
    {
        DocKind.Embedded => $"{Json}.ReadEmbedded<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(v)",
        DocKind.List when field.ElementKind == DocKind.Embedded => $"{Json}.ReadEmbeddedList<{field.Embedded!.ToDisplayString(DocModel.Qualified)}>(v)",
        DocKind.List => $"{Json}.ReadList<{field.Element!.ToDisplayString(DocModel.Qualified)}>(v, static x => {ReadScalar(field.ElementKind, "x", field.Element!.ToDisplayString(DocModel.Qualified))})",
        _ => ReadScalar(field.Kind, "v", field.ScalarName),
    };

    private static string ReadScalar(DocKind kind, string element, string typeName) => kind switch
    {
        DocKind.String => $"{element}.GetString()!",
        DocKind.Int32 => $"{element}.GetInt32()",
        DocKind.Int64 => $"{element}.GetInt64()",
        DocKind.UInt64 => $"unchecked((ulong){element}.GetInt64())",
        DocKind.Double => $"{element}.GetDouble()",
        DocKind.Decimal => $"{Json}.ReadDecimal({element})",
        DocKind.Boolean => $"{element}.GetBoolean()",
        DocKind.Guid => $"{Json}.ReadGuid({element})",
        DocKind.DateTimeOffset => $"{Json}.ReadInstant({element})",
        DocKind.DateTime => $"{Json}.ReadDateTime({element})",
        DocKind.Enum => $"({typeName}){element}.GetInt32()",
        _ => $"default! /* {kind} */",
    };

    /// <summary>The key as Cosmos DB's <c>id</c> (a string): a Guid's text, a string, a number's invariant text.</summary>
    internal static string IdText(DocField key, string value) => key.Kind switch
    {
        DocKind.Guid => $"{value}.ToString(\"D\")",
        DocKind.String => value,
        _ => $"{value}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)",
    };

    private static string ParseId(DocField key) => key.Kind switch
    {
        DocKind.Guid => "global::System.Guid.Parse(v.GetString()!)",
        DocKind.String => "v.GetString()!",
        DocKind.Int32 => "int.Parse(v.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture)",
        _ => "long.Parse(v.GetString()!, global::System.Globalization.CultureInfo.InvariantCulture)",
    };
}
