using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     ListPending{Parent}CommentsQuery: the comments on a parent that await approval, oldest first, paged; a declared query the Sql or documents
///     engine compiles, which the moderation queue's action runs through the child's repository.
/// </summary>
/// <remarks>
///     Only where an engine persists the comment: with EF Core the action reads the queue from its <c>DbContext</c>. The status is a constant
///     the query reads, not an input, so the query lists what awaits approval and nothing else.
/// </remarks>
internal sealed class CommentPendingQueryTemplate : CSharpTemplate
{
    private readonly CommentTraitModel _model;
    private readonly string _queryTypeName;

    public CommentPendingQueryTemplate(CommentTraitModel model)
    {
        _model = model;
        _queryTypeName = $"ListPending{model.ParentTypeName}CommentsQuery";
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasComments(RequireApproval = true)] moderation queue for {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_queryTypeName, "QueryClass", _model.ParentNamespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Persistence.Query.Attributes");
        AddUsing("Pragmatic.Persistence.Query");
        AddUsing("Pragmatic.Comments");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary($"SG-generated: the comments on a <see cref=\"{_model.ParentTypeName}\"/> that await approval, oldest first.");
        AppendLine($"[Query<{_model.CommentTypeName}, {_model.CommentTypeName}Dto>]");
        Class(_queryTypeName, () =>
        {
            AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
            AppendLine("[Filter]");
            AppendLine("public CommentStatus Status => CommentStatus.PendingApproval;");
            AppendLine("public int Page { get; init; } = 1;");
            // Clamped like the comment list: an unbounded page size let one request pull the whole queue.
            AppendLine("private int _pageSize = 20;");
            AppendLine("public int PageSize { get => _pageSize; init => _pageSize = value is < 1 or > 200 ? 20 : value; }");
            AppendLine("[Sort(MapTo = \"CreatedAt\", DefaultDirection = SortDirection.Ascending)]");
            AppendLine("public SortDirection? CreatedAtSort { get; init; }");
        },
        accessModifier: AccessModifier.Public,
        modifiers: new ClassModifiers { Partial = true });
    }
}
