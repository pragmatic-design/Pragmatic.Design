using Pragmatic.Documents.Csv.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Documents.Csv.Generator.Templates;

/// <summary>Generates a nested Csv class with typed Write/Read/Headers.</summary>
internal sealed class CsvSerializerTemplate(CsvTypeModel model) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Documents.Csv";

    public override Artifact RenderOutput()
    {
        var content = ToString();
        // Include the namespace in the hint so two same-named types in different namespaces don't
        // collide on AddSource (which would drop one of the generated files).
        var prefix = model.Namespace.Length > 0 ? $"{model.Namespace}." : "";
        return Artifact.FromString($"{prefix}{model.TypeName}.CsvSerializer.g.cs", content ?? "");
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Globalization");
        AddUsing("System.Collections.Generic");
        AddUsing("System.IO");
        AddUsing("System.Linq");
        AddUsing("Pragmatic.Documents.Csv");
        AddUsing("Pragmatic.Documents.Spreadsheet");

        if (model.Namespace.Length > 0)
            AppendNamespace(model.Namespace);

        AppendLine();

        // partial type declaration
        AppendLine($"{model.Accessibility} partial {model.TypeKeyword} {model.TypeName}");
        Block(RenderOuterBody);
    }

    private void RenderOuterBody()
    {
        XmlSummary("Generated CSV serializer — zero reflection, AOT-safe.");
        AppendLine("public static class Csv");
        Block(RenderCsvClass);
    }

    private void RenderCsvClass()
    {
        RenderHeaders();
        AppendLine();
        RenderWriteMethod();
        AppendLine();
        RenderWriteToArrayMethod();
        AppendLine();
        RenderReadStreamMethod();
        AppendLine();
        RenderReadBytesMethod();
        AppendLine();
        RenderFormatRow();
        AppendLine();
        RenderGetField();
        AppendLine();
        RenderParseRow();
    }

    private void RenderHeaders()
    {
        var headers = string.Join(", ", model.Properties.Select(p => $"\"{Escape(p.Header)}\""));
        AppendLine($"public static readonly string[] Headers = [{headers}];");
    }

    private void RenderWriteMethod()
    {
        XmlSummary($"Write a collection of <see cref=\"{model.TypeName}\"/> to a stream as CSV.");
        AppendLine($"public static void Write(Stream stream, IEnumerable<{model.TypeName}> items, CsvOptions? options = null)");
        Block(() =>
        {
            AppendLine("options ??= CsvOptions.Default;");
            // Materialise each row's fields, then hand off to CsvWriter (which does the RFC-4180
            // quoting and formula-injection protection).
            AppendLine($"var rows = new System.Collections.Generic.List<IReadOnlyList<string?>>();");
            AppendLine($"foreach (var item in items) rows.Add(FormatRow(item, options));");
            AppendLine("CsvWriter.Write(stream, Headers, rows, options);");
        });
    }

    private void RenderWriteToArrayMethod()
    {
        XmlSummary($"Write a collection of <see cref=\"{model.TypeName}\"/> to a byte array as CSV.");
        AppendLine($"public static byte[] WriteToArray(IEnumerable<{model.TypeName}> items, CsvOptions? options = null)");
        Block(() =>
        {
            AppendLine("using var ms = new MemoryStream();");
            AppendLine("Write(ms, items, options);");
            AppendLine("return ms.ToArray();");
        });
    }

    private void RenderReadStreamMethod()
    {
        XmlSummary($"Read CSV from a stream into a list of <see cref=\"{model.TypeName}\"/>.");
        AppendLine($"public static List<{model.TypeName}> Read(Stream stream, CsvOptions? options = null)");
        Block(() =>
        {
            AppendLine("options ??= CsvOptions.Default;");
            AppendLine("var (headers, rows) = CsvReader.Read(stream, options);");
            AppendLine("var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);");
            AppendLine("for (var i = 0; i < headers.Length; i++) map[headers[i]] = i;");
            AppendLine($"return rows.Select(row => ParseRow(row, map, options)).ToList();");
        });
    }

    private void RenderReadBytesMethod()
    {
        XmlSummary($"Read CSV from a byte array into a list of <see cref=\"{model.TypeName}\"/>.");
        AppendLine($"public static List<{model.TypeName}> Read(byte[] data, CsvOptions? options = null)");
        Block(() =>
        {
            AppendLine("using var ms = new MemoryStream(data);");
            AppendLine("return Read(ms, options);");
        });
    }

    private void RenderFormatRow()
    {
        AppendLine($"private static string?[] FormatRow({model.TypeName} item, CsvOptions options)");
        Block(() =>
        {
            AppendLine("return");
            AppendLine("[");
            IncreaseIndent();
            for (var i = 0; i < model.Properties.Length; i++)
            {
                var p = model.Properties[i];
                var comma = i < model.Properties.Length - 1 ? "," : "";
                AppendLine($"{FormatExpression(p)}{comma}");
            }
            DecreaseIndent();
            AppendLine("];");
        });
    }

    private void RenderGetField()
    {
        AppendLine("private static string GetField(string[] fields, Dictionary<string, int> map, string header)");
        AppendLine("    => map.TryGetValue(header, out var idx) && idx < fields.Length ? fields[idx] : \"\";");
    }

    private void RenderParseRow()
    {
        AppendLine($"private static {model.TypeName} ParseRow(string[] fields, Dictionary<string, int> map, CsvOptions options)");
        Block(() =>
        {
            // Only round-trippable properties are read back. Unsupported ('Other') types are write-only
            // (they cannot be parsed from text into their CLR type) so they are left at their default on
            // read — omitting them keeps the generated code compiling. A PRAG1900 warning flags them.
            // A computed (get-only) property is left out too: it is a column of the export, and it
            // recomputes from the ones that are read — assigning it was CS0200.
            var readable = model.Properties.Where(p => p.Kind != CsvPropertyKind.Other && p.IsWritable).ToList();

            // Pre-declare temp variables for nullable parsing
            var nullable = readable.Where(p => p.IsNullable && p.Kind != CsvPropertyKind.String).ToList();
            if (nullable.Count > 0)
            {
                foreach (var p in nullable)
                    AppendLine($"string __{p.PropertyName};");
                AppendLine();
            }

            AppendLine($"return new {model.TypeName}");
            AppendLine("{");
            IncreaseIndent();
            for (var i = 0; i < readable.Count; i++)
            {
                var p = readable[i];
                var comma = i < readable.Count - 1 ? "," : "";
                var getField = $"GetField(fields, map, \"{Escape(p.Header)}\")";
                AppendLine($"{p.PropertyName} = {ParseExpression(p, getField)}{comma}");
            }
            DecreaseIndent();
            AppendLine("};");
        });
    }

    // --- Code generation helpers ---

    // The fully-qualified type used for Enum.TryParse — the property type without a trailing nullable '?'.
    private static string EnumType(CsvPropertyModel p) => p.TypeName.TrimEnd('?');

    private static string FormatExpression(CsvPropertyModel p)
    {
        var accessor = $"item.{p.PropertyName}";

        // Unsupported types are written as their text form (export only); read leaves them at default.
        if (p.Kind == CsvPropertyKind.Other)
            return $"{accessor}?.ToString() ?? \"\"";

        if (p.IsNullable && p.Kind != CsvPropertyKind.String)
            return FormatNullableExpression(p, accessor);

        return p.Kind switch
        {
            CsvPropertyKind.String => $"{accessor} ?? \"\"",
            CsvPropertyKind.Bool => $"{accessor} ? \"true\" : \"false\"",
            CsvPropertyKind.DateTime or CsvPropertyKind.DateTimeOffset =>
                p.Format is not null
                    ? $"{accessor}.ToString(\"{Escape(p.Format)}\", options.Culture)"
                    : $"{accessor}.ToString(options.DateFormat, options.Culture)",
            CsvPropertyKind.Int or CsvPropertyKind.Long =>
                $"{accessor}.ToString(options.Culture)",
            CsvPropertyKind.Double or CsvPropertyKind.Float or CsvPropertyKind.Decimal =>
                p.Format is not null
                    ? $"{accessor}.ToString(\"{Escape(p.Format)}\", options.Culture)"
                    : $"{accessor}.ToString(options.Culture)",
            CsvPropertyKind.Enum or CsvPropertyKind.Guid or CsvPropertyKind.TimeSpan =>
                $"{accessor}.ToString()",
            _ => $"{accessor}?.ToString() ?? \"\""
        };
    }

    private static string FormatNullableExpression(CsvPropertyModel p, string accessor)
    {
        var inner = p.Kind switch
        {
            CsvPropertyKind.Bool => $"{accessor}.Value ? \"true\" : \"false\"",
            CsvPropertyKind.DateTime or CsvPropertyKind.DateTimeOffset =>
                p.Format is not null
                    ? $"{accessor}.Value.ToString(\"{Escape(p.Format)}\", options.Culture)"
                    : $"{accessor}.Value.ToString(options.DateFormat, options.Culture)",
            CsvPropertyKind.Enum or CsvPropertyKind.Guid or CsvPropertyKind.TimeSpan =>
                $"{accessor}.Value.ToString()",
            _ => p.Format is not null
                ? $"{accessor}.Value.ToString(\"{Escape(p.Format)}\", options.Culture)"
                : $"{accessor}.Value.ToString(options.Culture)"
        };
        return $"{accessor}.HasValue ? {inner} : \"\"";
    }

    private static string ParseExpression(CsvPropertyModel p, string fieldExpr)
    {
        if (p.IsNullable && p.Kind != CsvPropertyKind.String)
            return ParseNullableExpression(p, fieldExpr);

        return p.Kind switch
        {
            CsvPropertyKind.String => fieldExpr,
            CsvPropertyKind.Int => $"(int.TryParse({fieldExpr}, NumberStyles.Integer, options.Culture, out var __{p.PropertyName}Int) ? __{p.PropertyName}Int : default)",
            CsvPropertyKind.Long => $"(long.TryParse({fieldExpr}, NumberStyles.Integer, options.Culture, out var __{p.PropertyName}Long) ? __{p.PropertyName}Long : default)",
            CsvPropertyKind.Double => $"(double.TryParse({fieldExpr}, NumberStyles.Float | NumberStyles.AllowThousands, options.Culture, out var __{p.PropertyName}Double) ? __{p.PropertyName}Double : default)",
            CsvPropertyKind.Float => $"(float.TryParse({fieldExpr}, NumberStyles.Float | NumberStyles.AllowThousands, options.Culture, out var __{p.PropertyName}Float) ? __{p.PropertyName}Float : default)",
            CsvPropertyKind.Decimal => $"(decimal.TryParse({fieldExpr}, NumberStyles.Number, options.Culture, out var __{p.PropertyName}Decimal) ? __{p.PropertyName}Decimal : default)",
            CsvPropertyKind.DateTime =>
                p.Format is not null
                    ? $"(DateTime.TryParseExact({fieldExpr}, \"{Escape(p.Format)}\", options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dt) ? __{p.PropertyName}Dt : default)"
                    : $"(DateTime.TryParseExact({fieldExpr}, options.DateFormat, options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dt) ? __{p.PropertyName}Dt : default)",
            CsvPropertyKind.DateTimeOffset =>
                p.Format is not null
                    ? $"(DateTimeOffset.TryParseExact({fieldExpr}, \"{Escape(p.Format)}\", options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dto) ? __{p.PropertyName}Dto : default)"
                    : $"(DateTimeOffset.TryParseExact({fieldExpr}, options.DateFormat, options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dto) ? __{p.PropertyName}Dto : default)",
            CsvPropertyKind.Bool => $"(bool.TryParse({fieldExpr}, out var __{p.PropertyName}Bool) ? __{p.PropertyName}Bool : default)",
            CsvPropertyKind.Enum => $"(Enum.TryParse<{EnumType(p)}>({fieldExpr}, true, out var __{p.PropertyName}Enum) ? __{p.PropertyName}Enum : default)",
            CsvPropertyKind.Guid => $"(Guid.TryParse({fieldExpr}, out var __{p.PropertyName}Guid) ? __{p.PropertyName}Guid : default)",
            CsvPropertyKind.TimeSpan => $"(TimeSpan.TryParse({fieldExpr}, options.Culture, out var __{p.PropertyName}Ts) ? __{p.PropertyName}Ts : default)",
            _ => fieldExpr
        };
    }

    private static string ParseNullableExpression(CsvPropertyModel p, string fieldExpr)
    {
        var varName = $"__{p.PropertyName}";
        var innerParse = p.Kind switch
        {
            CsvPropertyKind.Int => $"(int.TryParse({varName}, NumberStyles.Integer, options.Culture, out var __{p.PropertyName}Int) ? __{p.PropertyName}Int : default(int))",
            CsvPropertyKind.Long => $"(long.TryParse({varName}, NumberStyles.Integer, options.Culture, out var __{p.PropertyName}Long) ? __{p.PropertyName}Long : default(long))",
            CsvPropertyKind.Double => $"(double.TryParse({varName}, NumberStyles.Float | NumberStyles.AllowThousands, options.Culture, out var __{p.PropertyName}Double) ? __{p.PropertyName}Double : default(double))",
            CsvPropertyKind.Float => $"(float.TryParse({varName}, NumberStyles.Float | NumberStyles.AllowThousands, options.Culture, out var __{p.PropertyName}Float) ? __{p.PropertyName}Float : default(float))",
            CsvPropertyKind.Decimal => $"(decimal.TryParse({varName}, NumberStyles.Number, options.Culture, out var __{p.PropertyName}Decimal) ? __{p.PropertyName}Decimal : default(decimal))",
            CsvPropertyKind.DateTime =>
                p.Format is not null
                    ? $"(DateTime.TryParseExact({varName}, \"{Escape(p.Format)}\", options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dt) ? __{p.PropertyName}Dt : default(DateTime))"
                    : $"(DateTime.TryParseExact({varName}, options.DateFormat, options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dt) ? __{p.PropertyName}Dt : default(DateTime))",
            CsvPropertyKind.DateTimeOffset =>
                p.Format is not null
                    ? $"(DateTimeOffset.TryParseExact({varName}, \"{Escape(p.Format)}\", options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dto) ? __{p.PropertyName}Dto : default(DateTimeOffset))"
                    : $"(DateTimeOffset.TryParseExact({varName}, options.DateFormat, options.Culture, DateTimeStyles.None, out var __{p.PropertyName}Dto) ? __{p.PropertyName}Dto : default(DateTimeOffset))",
            CsvPropertyKind.Bool => $"(bool.TryParse({varName}, out var __{p.PropertyName}Bool) ? __{p.PropertyName}Bool : default(bool))",
            CsvPropertyKind.Enum => $"(Enum.TryParse<{EnumType(p)}>({varName}, true, out var __{p.PropertyName}Enum) ? __{p.PropertyName}Enum : default({EnumType(p)}))",
            CsvPropertyKind.Guid => $"(Guid.TryParse({varName}, out var __{p.PropertyName}Guid) ? __{p.PropertyName}Guid : default(Guid))",
            CsvPropertyKind.TimeSpan => $"(TimeSpan.TryParse({varName}, options.Culture, out var __{p.PropertyName}Ts) ? __{p.PropertyName}Ts : default(TimeSpan))",
            _ => varName
        };

        return $"({varName} = {fieldExpr}).Length > 0 ? {innerParse} : null";
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
