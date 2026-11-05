using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

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

        foreach (var accessor in model.Accessors)
            AppendLine("    " + accessor);
        if (model.Accessors.Count > 0)
            AppendLine();

        // The fields in order, the key first: a document's _id is its first field. In a hierarchy a type below this one writes its own document,
        // and this one's carries its discriminator.
        AppendLine($"    static void {contract}.WriteFields({Bson}.BsonWriter w, {name} e)");
        AppendLine("    {");
        RenderWriteDispatch("WriteFields");
        foreach (var field in model.Fields.OrderBy(f => f.IsKey ? 0 : 1))
            AppendLine("        " + field.BsonWrite + ";");
        if (model.Inheritance is { } inheritance)
            AppendLine($"        {Runtime}.Conversion.DocValues.Write(w, {inheritance.FieldLiteral}, {inheritance.NameLiteral});");
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
        if (RenderReadDispatch("document", $"{Runtime}.Conversion.DocInheritance.Discriminator(document, {model.Inheritance?.FieldLiteral}u8)", "ReadDocument"))
        {
            AppendLine("    }");
            return;
        }

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
            AppendLine($"                {field.Assign(field.BsonRead)}");
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
        AppendLine($"    private static readonly {Runtime}.Filters.DocFamily[] __documentFamilies =");
        AppendLine("    [");
        foreach (var family in model.Families)
            AppendLine("        " + family + ",");
        AppendLine("    ];");
        AppendLine();
        AppendLine($"    static {Runtime}.Filters.DocFamily[] {contract}.Families => __documentFamilies;");
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
            RenderWriteDispatch("WriteJson");
            AppendLine("        w.WriteStartObject();");
            AppendLine($"        w.WriteString(\"id\", {model.IdText});");
            foreach (var field in model.Fields)
            {
                if (!field.IsKey)
                    AppendLine("        " + field.JsonWrite + ";");
            }

            if (model.Inheritance is { } inheritance)
                AppendLine($"        w.WriteString({inheritance.FieldLiteral}, {inheritance.NameLiteral});");
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
        if (RenderReadDispatch("document", $"{Runtime}.Conversion.DocInheritance.Discriminator(document, {model.Inheritance?.FieldLiteral})", "ReadJson"))
        {
            AppendLine("    }");
            return;
        }

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
            AppendLine($"                    {field.Assign(field.JsonRead)}");
            AppendLine("                    break;");
        }

        AppendLine("            }");
        AppendLine("        }");
        AppendLine();
        AppendLine("        return e;");
        AppendLine("    }");
    }

    /// <summary>A hierarchy's type with types below it: an entity of one of them is written by that type's own <paramref name="method" />.</summary>
    private void RenderWriteDispatch(string method)
    {
        if (model.Inheritance is not { Below.Count: > 0 } inheritance)
            return;
        AppendLine("        switch (e)");
        AppendLine("        {");
        var i = 0;
        foreach (var below in inheritance.Below)
            AppendLine($"            case {below.FullName} x{i}: {Runtime}.Conversion.DocInheritance.{method}<{below.FullName}>(w, x{i++}); return;");
        AppendLine("        }");
        AppendLine();
    }

    /// <summary>
    ///     A hierarchy's type with types below it: a document whose discriminator names one of them is read by that type's own
    ///     <paramref name="method" />. True when nothing follows (an abstract type: a document is always one of the types below it).
    /// </summary>
    private bool RenderReadDispatch(string document, string discriminator, string method)
    {
        if (model.Inheritance is not { } inheritance || (inheritance.Below.Count == 0 && !inheritance.IsAbstract))
            return false;
        if (inheritance.Below.Count > 0)
        {
            AppendLine($"        switch ({discriminator})");
            AppendLine("        {");
            foreach (var below in inheritance.Below)
                AppendLine($"            case {below.NameLiteral}: return {Runtime}.Conversion.DocInheritance.{method}<{below.FullName}>({document});");
            AppendLine("        }");
            AppendLine();
        }

        if (!inheritance.IsAbstract)
            return false;
        AppendLine($"        throw new global::System.InvalidOperationException(\"The document's discriminator names no type below the abstract {model.TypeName}.\");");
        return true;
    }
}
