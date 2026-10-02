using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

internal sealed class AttachmentListQueryTemplate : CSharpTemplate
{
    private readonly AttachmentTraitModel _model;
    public AttachmentListQueryTemplate(AttachmentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments] list query for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType($"List{_model.ParentTypeName}AttachmentsQuery", "QueryClass", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Persistence.Query.Attributes");
        AddUsing("Pragmatic.Persistence.Query");
        AddUsing("Pragmatic.Attachments");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var queryName = $"List{_model.ParentTypeName}AttachmentsQuery";
        var dtoName = $"{_model.AttachmentTypeName}Dto";

        XmlSummary($"SG-generated: lists attachments on a <see cref=\"{_model.ParentTypeName}\"/> with paging.");
        AppendLine($"[Query<{_model.AttachmentTypeName}, {dtoName}>]");
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
            AppendLine("public SortDirection? UploadedAtSort { get; init; }");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
