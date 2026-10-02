using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class NoteListQueryTemplate : CSharpTemplate
{
    private readonly NoteTraitModel _model;
    public NoteListQueryTemplate(NoteTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasNotes] list query for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType($"List{_model.ParentTypeName}NotesQuery", "QueryClass", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Persistence.Query.Attributes");
        AddUsing("Pragmatic.Persistence.Query");
        AddUsing("Pragmatic.Notes");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var queryName = $"List{_model.ParentTypeName}NotesQuery";
        var dtoName = $"{_model.NoteTypeName}Dto";

        XmlSummary($"SG-generated: lists notes on a <see cref=\"{_model.ParentTypeName}\"/> with paging.");
        AppendLine($"[Query<{_model.NoteTypeName}, {dtoName}>]");
        Class(queryName, () =>
        {
            AppendLine($"public required {_model.SimpleIdType} {_model.ParentTypeName}Id {{ get; init; }}");
            AppendLine("public int Page { get; init; } = 1;");
            // Clamped: an unbounded pageSize let one request pull the whole table. An explicit backing
            // field rather than the `field` keyword — the generated code has to compile under whatever
            // LangVersion the consumer uses, not just the one this repo builds with.
            AppendLine("private int _pageSize = 20;");
            AppendLine("public int PageSize { get => _pageSize; init => _pageSize = value is < 1 or > 200 ? 20 : value; }");
            // Paging without an order is not reproducible: PostgreSQL may return a row on page 2
            // that already appeared on page 1. Newest-first by default, still overridable.
            AppendLine("public SortDirection? CreatedAtSort { get; init; }");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
