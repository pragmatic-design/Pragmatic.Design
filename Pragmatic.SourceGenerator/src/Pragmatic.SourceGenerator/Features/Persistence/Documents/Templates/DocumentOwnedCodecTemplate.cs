using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     An owned type's codec: a static class that writes the object as a nested document (or null), and reads it back field by field into locals
///     before building it, through its constructor when it has no parameterless one. The BSON half MongoDB reads, the JSON half Cosmos DB reads.
/// </summary>
/// <remarks>
///     The reader keeps each value in a local rather than assigning the object's members as it goes, because a value object's members are
///     <c>init</c> or set by its constructor. Every member is written, so a field the document lacks was written by another program: the member is
///     read as its type's default, not as the default the type's own initializer gives it.
/// </remarks>
internal sealed class DocumentOwnedCodecTemplate(DocumentOwnedCodecModel model) : CSharpTemplate
{
    private const string Bson = "global::Pragmatic.Persistence.Documents.Mongo.Bson";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string Element = "global::System.Text.Json.JsonElement";

    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the nested document codec of " + model.FullName;

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var name = model.FullName;
        AppendLine("namespace Pragmatic.Persistence.Documents.Generated;");
        AppendLine();
        AppendLine($"internal static class {model.CodecName}");
        AppendLine("{");
        AppendLine($"    public static void Write({Bson}.BsonWriter w, string name, {name}? value)");
        AppendLine("    {");
        AppendLine("        if (value is null)");
        AppendLine("        {");
        AppendLine("            w.WriteNull(name);");
        AppendLine("            return;");
        AppendLine("        }");
        AppendLine();
        AppendLine("        var e = value;");
        AppendLine("        w.StartDocument(name);");
        foreach (var field in model.Fields)
            AppendLine("        " + field.BsonWrite + ";");
        AppendLine("        w.EndDocument();");
        AppendLine("    }");
        AppendLine();
        RenderRead(name);
        AppendLine();
        AppendLine($"    public static void WriteJson({Writer} w, string name, {name}? value)");
        AppendLine("    {");
        AppendLine("        w.WritePropertyName(name);");
        AppendLine("        if (value is null)");
        AppendLine("        {");
        AppendLine("            w.WriteNullValue();");
        AppendLine("            return;");
        AppendLine("        }");
        AppendLine();
        AppendLine("        var e = value;");
        AppendLine("        w.WriteStartObject();");
        foreach (var field in model.Fields)
            AppendLine("        " + field.JsonWrite + ";");
        AppendLine("        w.WriteEndObject();");
        AppendLine("    }");
        AppendLine();
        RenderReadJson(name);
        AppendLine("}");
    }

    private void RenderRead(string name)
    {
        AppendLine($"    public static {name} Read(global::System.ReadOnlySpan<byte> document)");
        AppendLine("    {");
        AppendLine($"        var r = new {Bson}.BsonReader(document);");
        RenderLocals();
        AppendLine("        while (r.ReadElement(out var type, out var name))");
        AppendLine("        {");
        AppendLine($"            if (type == {Bson}.BsonType.Null)");
        AppendLine("            {");
        AppendLine("                r.Skip(); // a null is the member's default");
        AppendLine("                continue;");
        AppendLine("            }");
        AppendLine();
        var first = true;
        foreach (var (field, index) in model.Fields.Select((f, i) => (f, i)))
        {
            AppendLine($"            {(first ? "if" : "else if")} (global::System.MemoryExtensions.SequenceEqual(name, {field.BsonNameLiteral}u8))");
            AppendLine($"                p{index} = {field.BsonRead};");
            first = false;
        }

        AppendLine("            else");
        AppendLine("                r.Skip(); // a field the type does not have");
        AppendLine("        }");
        AppendLine();
        AppendLine($"        return {Construct(name)};");
        AppendLine("    }");
    }

    private void RenderReadJson(string name)
    {
        AppendLine($"    public static {name} ReadJson({Element} document)");
        AppendLine("    {");
        RenderLocals();
        AppendLine("        foreach (var property in document.EnumerateObject())");
        AppendLine("        {");
        AppendLine("            var v = property.Value;");
        AppendLine("            if (v.ValueKind == global::System.Text.Json.JsonValueKind.Null)");
        AppendLine("                continue; // a null is the member's default");
        AppendLine("            switch (property.Name)");
        AppendLine("            {");
        foreach (var (field, index) in model.Fields.Select((f, i) => (f, i)))
        {
            AppendLine($"                case {field.JsonNameLiteral}:");
            AppendLine($"                    p{index} = {field.JsonRead};");
            AppendLine("                    break;");
        }

        AppendLine("            }");
        AppendLine("        }");
        AppendLine();
        AppendLine($"        return {Construct(name)};");
        AppendLine("    }");
    }

    private void RenderLocals()
    {
        foreach (var (field, index) in model.Fields.Select((f, i) => (f, i)))
            AppendLine($"        {field.TypeName} p{index} = default!;");
        AppendLine();
    }

    /// <summary><c>new T { A = p0, … }</c>, or <c>new T(p…) { the others }</c> through the constructor whose parameters are its members.</summary>
    private string Construct(string name)
    {
        var fields = model.Fields.Select((f, i) => (f.PropertyName, Local: "p" + i)).ToList();
        var constructor = model.Constructor.ToList();
        var initialized = fields.Where(f => !constructor.Contains(f.PropertyName)).Select(f => $"{f.PropertyName} = {f.Local}").ToList();
        var initializer = initialized.Count == 0 ? "" : $" {{ {string.Join(", ", initialized)} }}";
        if (constructor.Count == 0)
            return $"new {name}(){initializer}";
        var arguments = constructor.Select(member => fields.First(f => f.PropertyName == member).Local);
        return $"new {name}({string.Join(", ", arguments)}){initializer}";
    }
}
