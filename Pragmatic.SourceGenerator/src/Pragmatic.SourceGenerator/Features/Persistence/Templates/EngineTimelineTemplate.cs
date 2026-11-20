using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     <c>[GenerateTimeline]</c> on the Sql and documents engines (#898): <c>GetTimelineAsync</c> over the entity's read repository, with the
///     same entry type and the same result as EF Core's LAG/LEAD CTE (<see cref="TimelineQueryTemplate" />).
/// </summary>
/// <remarks>
///     <para>
///         The periods are read through the repository with the entity's temporal filter lifted (its history scope), then linked in memory: per
///         parent, ordered by <c>ValidFrom</c>, each with the <c>ValidTo</c> of the one before and the <c>ValidFrom</c> of the one after, the
///         whole ordered by <c>ValidFrom</c>. That is what the CTE computes (<c>TimelineCteSql</c>: <c>LAG(ValidTo)</c>, <c>LEAD(ValidFrom)</c>,
///         <c>PARTITION BY</c> the parent's key). The rows read are the rows returned, one entry per period, so the window computed here costs
///         no more transfer than one computed by the database; and one implementation serves both engines through the repository they serve.
///     </para>
///     <para>
///         The caller's other filters apply, where EF Core's raw SQL goes around all of them: an engine's read is never wider than its caller's.
///     </para>
/// </remarks>
internal sealed class EngineTimelineTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public EngineTimelineTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} timeline from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GenerateTimeline] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "TimelineQuery", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, IsTemporalRelation: true };

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary($"Generated timeline of <see cref=\"{_model.TypeName}\"/>, read through its repository.");
        Class(NamingHelper.AppendSuffix(_model.TypeName, "TimelineExtensions"), RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        var entityType = $"global::{_model.FullTypeName}";
        var entryType = $"{_model.TypeName}TimelineEntry";
        var partition = string.IsNullOrEmpty(_model.TemporalParentFkProperty) ? null : _model.TemporalParentFkProperty;

        XmlSummary(TimelineQueryTemplate.GetTimelineAsyncSummary(_model.TypeName));
        XmlParam("repository", "The entity's read repository.");
        XmlParam("filters", "The request's filter toggle: the temporal filter is lifted through it for the read.");
        XmlParam("ct", "Cancels the read.");
        XmlReturns("One entry per validity period, ordered by ValidFrom.");
        AppendLine($"public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{entryType}>> GetTimelineAsync(");
        AppendLine($"    this global::Pragmatic.Persistence.Repository.IReadRepository<{entityType}> repository,");
        AppendLine("    global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle filters,");
        AppendLine("    global::System.Threading.CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"global::System.Collections.Generic.List<{entityType}> rows;");
            Comment("Closed periods too: the timeline is the history.");
            AppendLine($"using (global::{_model.FullTypeName}.IncludeHistory(filters))");
            AppendLine($"    rows = await repository.FindAsync(global::Pragmatic.Specification.Spec<{entityType}>.True, ct).ConfigureAwait(false);");
            AppendLine();
            AppendLine($"var timeline = new global::System.Collections.Generic.List<{entryType}>(rows.Count);");
            var groups = partition is null
                ? "new[] { rows }"
                : $"global::System.Linq.Enumerable.GroupBy(rows, static r => r.{partition})";
            Comment(partition is null
                ? "One partition: the entity has no parent key."
                : $"Per {partition}, as the CTE's PARTITION BY: one parent's handovers do not bleed into another's.");
            AppendLine($"foreach (var partition in {groups})");
            Block(() =>
            {
                AppendLine("var ordered = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.OrderBy(partition, static r => r.ValidFrom));");
                AppendLine("for (var i = 0; i < ordered.Count; i++)");
                Block(() =>
                {
                    AppendLine($"timeline.Add(new {entryType}");
                    AppendLine("{");
                    AppendLine("    ValidFrom = ordered[i].ValidFrom,");
                    AppendLine("    ValidTo = ordered[i].ValidTo,");
                    AppendLine("    PreviousValidTo = i > 0 ? ordered[i - 1].ValidTo : null,");
                    AppendLine("    NextValidFrom = i + 1 < ordered.Count ? ordered[i + 1].ValidFrom : null,");
                    AppendLine("});");
                });
            });
            AppendLine();
            AppendLine("timeline.Sort(static (a, b) => a.ValidFrom.CompareTo(b.ValidFrom));");
            AppendLine("return timeline;");
        });

        AppendLine();
        XmlSummary($"Timeline entry for <see cref=\"{_model.TypeName}\"/> with LAG/LEAD columns.");
        AppendLine($"public sealed record {entryType}");
        Block(() =>
        {
            foreach (var member in TimelineQueryTemplate.EntryMembers)
                AppendLine(member);
        });
    }
}
