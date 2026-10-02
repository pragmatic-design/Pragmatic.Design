using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     Generates the read-side list query for [HasTags]: <c>List{Parent}TagsQuery</c>.
///     The queried entity is the junction — that is what carries the parent FK to filter on.
/// </summary>
internal sealed class TagListQueryTemplate : CSharpTemplate
{
    private readonly TagTraitModel _model;
    public TagListQueryTemplate(TagTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasTags] list query for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.ListQueryTypeName, "QueryClass", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Persistence.Query.Attributes");
        AddUsing("Pragmatic.Persistence.Query");
        AddUsing("Pragmatic.Tags");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated: lists the tags on a <see cref=\"{_model.ParentTypeName}\"/> with paging.");
        AppendLine($"[Query<{_model.JunctionTypeName}, {_model.TagDtoTypeName}>]");
        Class(_model.ListQueryTypeName, () =>
        {
            AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
            AppendLine("public int Page { get; init; } = 1;");
            // Clamped: an unbounded pageSize let one request pull the whole table. An explicit backing
            // field rather than the `field` keyword — the generated code has to compile under whatever
            // LangVersion the consumer uses, not just the one this repo builds with.
            AppendLine("private int _pageSize = 20;");
            AppendLine("public int PageSize { get => _pageSize; init => _pageSize = value is < 1 or > 200 ? 20 : value; }");
            // Paging without an order is not reproducible: PostgreSQL may return a row on page 2
            // that already appeared on page 1. Newest-first by default, still overridable.
            AppendLine("public SortDirection? AddedAtSort { get; init; }");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
