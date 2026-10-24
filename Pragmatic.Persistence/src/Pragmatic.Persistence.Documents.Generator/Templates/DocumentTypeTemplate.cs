using System.Linq;
using Pragmatic.Persistence.Documents.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Documents.Generator.Templates;

/// <summary>
///     The other half of a [Document] or [Embedded] partial class: <c>IDocumentEntity&lt;T&gt;</c> (or <c>IEmbeddedDocument&lt;T&gt;</c>) implemented
///     explicitly, so the type's own members stay the developer's: the fields written in order (<c>_id</c> first), the document read field by
///     field comparing the UTF-8 names, the key and the version, and the JSON half Cosmos DB reads.
/// </summary>
internal sealed class DocumentTypeTemplate(DocumentTypeModel model) : CSharpTemplate
{
    private const string Runtime = "global::Pragmatic.Persistence.Documents";
    private const string Bson = "global::Pragmatic.Persistence.Documents.Mongo.Bson";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string Element = "global::System.Text.Json.JsonElement";

    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the document reader and writer of " + model.TypeName;

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var name = model.FullName;
        var contract = model.Contract;
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.TypeName} : {contract}");
        AppendLine("{");
        if (!model.IsEmbedded)
        {
            AppendLine($"    static string {contract}.Collection => {model.CollectionLiteral};");
            AppendLine();
        }

        // The fields in order, the key first: a document's _id is its first field.
        AppendLine($"    static void {contract}.WriteFields({Bson}.BsonWriter w, {name} e)");
        AppendLine("    {");
        foreach (var field in model.Fields.OrderBy(f => f.IsKey ? 0 : 1))
            AppendLine("        " + field.BsonWrite + ";");
        AppendLine("    }");
        AppendLine();
        RenderReadDocument(name, contract);
        if (!model.IsEmbedded)
            RenderDocumentMembers(name, contract);
        RenderJson(name, contract);
        AppendLine("}");
    }

    private void RenderReadDocument(string name, string contract)
    {
        AppendLine($"    static {name} {contract}.ReadDocument(global::System.ReadOnlySpan<byte> document)");
        AppendLine("    {");
        AppendLine($"        var r = new {Bson}.BsonReader(document);");
        AppendLine($"        var e = new {name}();");
        AppendLine("        while (r.ReadElement(out var type, out var name))");
        AppendLine("        {");
        AppendLine($"            if (type == {Bson}.BsonType.Null)");
        AppendLine("            {");
        AppendLine("                r.Skip(); // a null leaves the property at its default");
        AppendLine("                continue;");
        AppendLine("            }");
        AppendLine();
        var first = true;
        foreach (var field in model.Fields)
        {
            AppendLine($"            {(first ? "if" : "else if")} (global::System.MemoryExtensions.SequenceEqual(name, {field.BsonNameLiteral}u8))");
            AppendLine($"                e.{field.PropertyName} = {field.BsonRead};");
            first = false;
        }

        if (first)
        {
            AppendLine("            r.Skip();");
        }
        else
        {
            AppendLine("            else");
            AppendLine("                r.Skip(); // a field the model does not have");
        }

        AppendLine("        }");
        AppendLine();
        AppendLine("        return e;");
        AppendLine("    }");
    }

    private void RenderDocumentMembers(string name, string contract)
    {
        AppendLine();
        AppendLine($"    static void {contract}.WriteKey({Bson}.BsonWriter w, string name, {name} e) => {model.KeyWrite};");
        AppendLine();
        AppendLine($"    static bool {contract}.Versioned => {(model.Versioned ? "true" : "false")};");
        AppendLine();
        AppendLine($"    static long {contract}.GetVersion({name} e) => {model.VersionGet};");
        AppendLine();
        AppendLine($"    static void {contract}.SetVersion({name} e, long version) {{ {model.VersionSet}}}");
        if (model.Families.Count == 0)
            return;

        // A Pragmatic entity's filter families, applied to every read of it.
        AppendLine();
        AppendLine($"    private static readonly {Runtime}.DocFamily[] __documentFamilies =");
        AppendLine("    [");
        foreach (var family in model.Families)
            AppendLine("        " + family + ",");
        AppendLine("    ];");
        AppendLine();
        AppendLine($"    static {Runtime}.DocFamily[] {contract}.Families => __documentFamilies;");
    }

    private void RenderJson(string name, string contract)
    {
        AppendLine();
        if (model.IsEmbedded)
        {
            AppendLine($"    static void {contract}.WriteJsonFields({Writer} w, {name} e)");
            AppendLine("    {");
            foreach (var field in model.Fields)
                AppendLine("        " + field.JsonWrite + ";");
            AppendLine("    }");
        }
        else
        {
            AppendLine($"    static void {contract}.WriteJson({Writer} w, {name} e)");
            AppendLine("    {");
            AppendLine("        w.WriteStartObject();");
            AppendLine($"        w.WriteString(\"id\", {model.IdText});");
            foreach (var field in model.Fields)
            {
                if (!field.IsKey)
                    AppendLine("        " + field.JsonWrite + ";");
            }

            AppendLine("        w.WriteEndObject();");
            AppendLine("    }");
            AppendLine();
            AppendLine($"    static string {contract}.Id({name} e) => {model.IdText};");
            AppendLine();
            AppendLine($"    static string {contract}.PartitionKeyPath => {model.PartitionKeyPathLiteral};");
            AppendLine();
            AppendLine($"    static void {contract}.WritePartitionKey({Writer} w, {name} e) => {model.PartitionKeyWrite};");
        }

        AppendLine();
        AppendLine($"    static {name} {contract}.ReadJson({Element} document)");
        AppendLine("    {");
        AppendLine($"        var e = new {name}();");
        AppendLine("        foreach (var property in document.EnumerateObject())");
        AppendLine("        {");
        AppendLine("            var v = property.Value;");
        AppendLine("            if (v.ValueKind == global::System.Text.Json.JsonValueKind.Null)");
        AppendLine("                continue; // a null leaves the property at its default");
        AppendLine("            switch (property.Name)");
        AppendLine("            {");
        foreach (var field in model.Fields)
        {
            AppendLine($"                case {field.JsonNameLiteral}:");
            AppendLine($"                    e.{field.PropertyName} = {field.JsonRead};");
            AppendLine("                    break;");
        }

        AppendLine("            }");
        AppendLine("        }");
        AppendLine();
        AppendLine("        return e;");
        AppendLine("    }");
    }
}
