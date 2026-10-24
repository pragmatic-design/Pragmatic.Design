using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     The schema of the Pragmatic entities the documents engine serves, per boundary: each collection's indexes, its partition key path on
///     Cosmos DB and its composite indexes, and <c>EnsureAsync(session)</c>, which applies them (<c>DocSchema</c>).
/// </summary>
internal sealed class DocSchemaTemplate(DocSchemaModel model) : CSharpTemplate
{
    private const string Runtime = "global::Pragmatic.Persistence.Documents";

    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the schema of the Pragmatic entities the documents engine serves";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"/// <summary>The collections of {model.Owner} ({model.Collections.Count}): their indexes, their Cosmos DB partition key and composite indexes.</summary>");
        AppendLine($"public static class {model.ClassName}");
        AppendLine("{");
        AppendLine($"    public static readonly global::System.Collections.Generic.IReadOnlyList<{Runtime}.DocCollectionSchema> Collections =");
        AppendLine("    [");
        foreach (var collection in model.Collections)
            AppendLine("        " + collection + ",");
        AppendLine("    ];");
        AppendLine();
        AppendLine("    /// <summary>Applies the schema to <paramref name=\"session\" />'s database; what the backend cannot apply is in the report, with its reason.</summary>");
        AppendLine($"    public static global::System.Threading.Tasks.Task<{Runtime}.DocSchemaReport> EnsureAsync({Runtime}.DocSession session, global::System.Threading.CancellationToken ct = default)");
        AppendLine($"        => {Runtime}.DocSchema.EnsureAsync(session, Collections, ct);");
        AppendLine("}");
    }
}
