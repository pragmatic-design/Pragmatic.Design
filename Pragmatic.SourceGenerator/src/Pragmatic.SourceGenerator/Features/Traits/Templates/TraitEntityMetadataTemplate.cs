using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
/// Generates [assembly: PragmaticMetadata(19 /* TraitEntities */, ...)] so the host SG
/// can discover trait-generated entities and include them in DbContext.
/// </summary>
internal sealed class TraitEntityMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<TraitEntityInfo> _traitEntities;
    private readonly string _rootNamespace;
    private readonly string _category;
    private readonly bool _efCoreRepositories;

    /// <param name="traitEntities">The entities to describe.</param>
    /// <param name="rootNamespace">Assembly name, kept for diagnostics context.</param>
    /// <param name="category">
    ///     Hint-name category, one per trait ("TraitEntities", "TagEntities", …). The four traits each
    ///     emit their own file. One shared hint patched afterwards by callers with a string Replace
    ///     would let a rename of the category here silently collapse all four onto the same hint name —
    ///     which makes Roslyn discard the generator's ENTIRE output with a mere warning.
    /// </param>
    /// <param name="efCoreRepositories">
    ///     Whether this compilation generates EF Core's repository of each child (<see cref="TraitRepositories" />, #888): then each one
    ///     keyed on its own id declares a <c>repositoryType</c>, which the host registers. False on an engine's compilation, whose children's
    ///     repositories its boundary registers itself, and which has no type of that name.
    /// </param>
    public TraitEntityMetadataTemplate(
        ImmutableArray<TraitEntityInfo> traitEntities, string rootNamespace, string category = "TraitEntities", bool efCoreRepositories = false)
    {
        _traitEntities = traitEntities;
        _rootNamespace = rootNamespace;
        _category = category;
        _efCoreRepositories = efCoreRepositories;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForMetadata(_category), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine(
            $"[assembly: PragmaticMetadata((MetadataCategory)19, \"1.0\", \"\"\"{BuildJson()}\"\"\")]");
    }

    /// <summary>
    ///     The document this template writes. Public because a host that declares a trait itself hands the same document to Composition
    ///     directly: the attribute is emitted into that compilation and can never be read back off it.
    /// </summary>
    public string BuildJson()
    {
        // Build JSON array of trait entity entries.
        // This payload is the ONLY channel to the host's DbContext and schema generation, so anything
        // the schema must reproduce has to travel here: relations became foreign keys and their indexes,
        // uniqueColumns became the tag de-duplication constraint, and isSoftDelete decides whether the
        // soft-delete index belongs on the table at all.
        var entries = new List<string>();
        foreach (var entity in _traitEntities)
        {
            var relations = string.Join(",", entity.Relations.AsImmutableArray().Select(static r =>
                $$"""{"column":"{{r.ForeignKeyColumn}}","references":"{{r.ReferencedTypeName}}","onDelete":"{{r.OnDelete}}"}"""));

            // The tag link has a composite key and no repository (TraitRepositories).
            var repository = _efCoreRepositories && entity.KeyColumns is null
                ? $",\"repositoryType\":\"{TraitRepositories.RepositoryTypeOf(entity.FullTypeName)}\""
                : "";

            entries.Add($$"""{"typeName":"{{entity.TypeName}}","namespace":"{{entity.Namespace}}","boundaryName":"{{entity.BoundaryName ?? ""}}","traitKind":"{{entity.TraitKind}}","keyColumns":"{{entity.KeyColumns ?? ""}}","uniqueColumns":"{{entity.UniqueColumns ?? ""}}","isSoftDelete":{{(entity.IsSoftDelete ? "true" : "false")}},"hasParentVisibilityFilter":{{(entity.HasParentVisibilityFilter ? "true" : "false")}},"hasInternalVisibilityFilter":{{(entity.HasInternalVisibilityFilter ? "true" : "false")}},"parentTenantNavigation":"{{entity.ParentTenantNavigation ?? ""}}","relations":[{{relations}}]{{repository}},"configTypeName":"{{entity.TypeName}}EntityConfig"}""");
        }

        return $"[{string.Join(",", entries)}]";
    }
}
