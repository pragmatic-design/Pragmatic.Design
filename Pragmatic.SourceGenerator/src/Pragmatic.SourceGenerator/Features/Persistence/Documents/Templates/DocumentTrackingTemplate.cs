using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     A Pragmatic entity's change detection as a document (<c>IDocumentTracked&lt;T&gt;</c>), a partial beside its reader and writer: a scalar is
///     kept and compared by value, a list or an embedded document by its BSON, so a change inside it is seen. Registered at module start, as the
///     Sql engine's maps are: a unit of work holds its entities as objects.
/// </summary>
internal sealed class DocumentTrackingTemplate(DocumentTrackingModel model) : CSharpTemplate
{
    private const string Runtime = "global::Pragmatic.Persistence.Documents";
    private const string Bson = "global::Pragmatic.Persistence.Documents.Mongo.Bson";

    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the change detection of " + model.TypeName;

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var name = model.FullName;
        var contract = $"{Runtime}.IDocumentTracked<{name}>";
        var fields = model.Fields;
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.TypeName} : {contract}");
        AppendLine("{");
        AppendLine($"    static object?[] {contract}.Snapshot({name} e) =>");
        AppendLine("    [");
        foreach (var field in fields)
            AppendLine($"        {field.Value},");
        AppendLine("    ];");
        AppendLine();
        AppendLine($"    static ulong {contract}.Changed({name} e, object?[] s)");
        AppendLine("    {");
        AppendLine("        ulong m = 0;");
        for (var i = 0; i < fields.Count; i++)
            AppendLine($"        if (!{Runtime}.DocTracking.Same(s[{i}], {fields[i].Value})) m |= 1UL << {i};");
        AppendLine("        return m;");
        AppendLine("    }");
        AppendLine();
        AppendLine($"    static void {contract}.WriteSet({Bson}.BsonWriter w, {name} e, ulong m)");
        AppendLine("    {");
        for (var i = 0; i < fields.Count; i++)
            AppendLine($"        if ((m & (1UL << {i})) != 0) {fields[i].Write};");
        AppendLine("    }");
        AppendLine();
        AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine($"    internal static void __RegisterDocumentTracker() => {Runtime}.DocTrackers.Register<{name}>();");
        AppendLine("}");
    }
}
