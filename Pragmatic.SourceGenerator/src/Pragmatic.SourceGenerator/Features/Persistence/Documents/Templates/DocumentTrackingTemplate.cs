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
            AppendLine($"        if (!{Runtime}.Writing.DocTracking.Same(s[{i}], {fields[i].Value})) m |= 1UL << {i};");
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
        var register = $"{Runtime}.Writing.DocTrackers.Register<{name}>();";
        if (model.SoftDeleteRules.Count == 0)
        {
            AppendLine($"    internal static {(model.HidesBase ? "new " : "")}void __RegisterDocumentTracker() => {register}");
        }
        else
        {
            AppendLine($"    internal static {(model.HidesBase ? "new " : "")}void __RegisterDocumentTracker()");
            AppendLine("    {");
            AppendLine($"        {register}");
            foreach (var rule in model.SoftDeleteRules)
                AppendLine($"        {SoftDeleteRule(rule, name)}");
            AppendLine("    }");
        }

        AppendLine("}");
    }

    /// <summary>
    ///     A relation's <c>OnSoftDelete</c>, written inside the referrer's partial so the key is emptied whatever its setter's access: the referrers
    ///     read through the referrer's own repository — the caller's filters, the soft-deleted documents left out — by a condition on the foreign
    ///     key compiled here for both stores, and tracked by the unit of work so its save writes what the rule changes on them.
    /// </summary>
    private static string SoftDeleteRule(DocSoftDeleteRuleModel rule, string name)
    {
        var mongo = $"static (w, s) => {{ w.StartDocument(\"{rule.ForeignKeyField}\"); {Runtime}.Conversion.DocValues.Write(w, \"$eq\", (global::System.Guid)s[0]!); w.EndDocument(); }}";
        var cosmos = Transforms.DocTypeEmitter.Literal($"c[\"{rule.ForeignKeyJson}\"] = {{p}}0");
        var json = $"static (w, i, s) => {Runtime}.Conversion.DocJson.WriteValue(w, (global::System.Guid)s[0]!)";
        var predicate = $"new {Runtime}.Planning.DocCompiledPredicate({mongo}, {cosmos}, 1, {json})";
        var specification = $"new {Runtime}.DocCompiledSpecification<{name}>({predicate}, new object?[] {{ key }}, d => d.{rule.ForeignKeyProperty} == key)";
        var referrers = $"static async (unitOfWork, key, ct) => new global::System.Collections.Generic.List<object>(await new DocRepository(unitOfWork).FindAsync({specification}, ct))";
        var clear = rule.ClearsKey ? $"static d => (({name})d).{rule.ForeignKeyProperty} = null" : "null";
        return $"{Runtime}.DocSoftDeleteRules.Register(new(typeof({rule.PrincipalType}), typeof({name}), global::Pragmatic.Persistence.Entity.DeleteBehavior.{rule.Behavior}, {referrers}, {clear}));";
    }
}
