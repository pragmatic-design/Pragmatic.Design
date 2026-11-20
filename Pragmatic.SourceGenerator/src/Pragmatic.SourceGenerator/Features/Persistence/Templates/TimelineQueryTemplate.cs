using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates GetTimeline() extension method using LAG/LEAD CTE
///     for temporal entities with ValidFrom/ValidTo.
/// </summary>
internal sealed class TimelineQueryTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TimelineQueryTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} Timeline CTE from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GenerateTimeline] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "TimelineQuery", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, IsTemporalRelation: true };

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var className = NamingHelper.AppendSuffix(_model.TypeName, "TimelineExtensions");

        XmlSummary($"Generated timeline query extensions for <see cref=\"{_model.TypeName}\"/>.");

        Class(className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        var entityType = $"global::{_model.FullTypeName}";

        XmlSummary("Gets the timeline view of this entity using a LAG/LEAD CTE, ordered by ValidFrom.");
        XmlParam("dbContext", "The database context.");
        XmlReturns("Timeline entries with previous/next validity information.");

        AppendLine($"public static global::System.Linq.IQueryable<{_model.TypeName}TimelineEntry> GetTimeline(this global::Microsoft.EntityFrameworkCore.DbContext dbContext)");
        Block(() =>
        {
            // Resolve the real table/column names from the EF model (honours [Table]/[Column] and
            // irregular plurals) and let TimelineCteSql delimit them per provider so the CTE
            // works on SQL Server, PostgreSQL and SQLite. Identifiers are metadata, never input.
            AppendLine($"var __entityType = dbContext.Model.FindEntityType(typeof({entityType}))");
            AppendLine($"    ?? throw new global::System.InvalidOperationException(\"Entity '{_model.TypeName}' is not mapped in this DbContext; GetTimeline() is unavailable.\");");
            AppendLine("var __table = __entityType.GetTableName()");
            AppendLine($"    ?? throw new global::System.InvalidOperationException(\"Entity '{_model.TypeName}' has no table mapping; GetTimeline() is unavailable.\");");
            AppendLine("var __validFrom = __entityType.FindProperty(\"ValidFrom\")?.GetColumnName() ?? \"ValidFrom\";");
            AppendLine("var __validTo = __entityType.FindProperty(\"ValidTo\")?.GetColumnName() ?? \"ValidTo\";");

            if (!string.IsNullOrEmpty(_model.TemporalParentFkProperty))
            {
                // Multi-parent relation: PARTITION BY the parent key so validity does not bleed across parents.
                AppendLine($"string? __partition = __entityType.FindProperty(\"{_model.TemporalParentFkProperty}\")?.GetColumnName() ?? \"{_model.TemporalParentFkProperty}\";");
            }
            else
            {
                AppendLine("string? __partition = null;");
            }

            AppendLine("var sql = global::Pragmatic.Persistence.EFCore.Query.TimelineCteSql.Build(");
            AppendLine("    dbContext.Database, __table, __validFrom, __validTo, __partition);");
            AppendLine($"return dbContext.Database.SqlQueryRaw<{_model.TypeName}TimelineEntry>(sql);");
        });

        AppendLine();

        // The engine-neutral read (#898): the same method on the Sql and documents engines (EngineTimelineTemplate), here over the CTE.
        XmlSummary(GetTimelineAsyncSummary(_model.TypeName));
        XmlParam("repository", "The entity's read repository: the generated Repository, over the boundary's DbContext.");
        XmlParam("filters", "The request's filter toggle. Unread here: the CTE is raw SQL and goes around the query filters.");
        XmlParam("ct", "Cancels the read.");
        XmlReturns("One entry per validity period, ordered by ValidFrom.");
        AppendLine($"public static global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{_model.TypeName}TimelineEntry>> GetTimelineAsync(");
        AppendLine($"    this global::Pragmatic.Persistence.Repository.IReadRepository<{entityType}> repository,");
        AppendLine("    global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle filters,");
        AppendLine("    global::System.Threading.CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"if (repository is not {entityType}.Repository efCore)");
            AppendLine($"    throw new global::System.InvalidOperationException(\"GetTimelineAsync reads {_model.TypeName}'s timeline through its generated EF Core repository; this one is \" + repository.GetType().Name + \".\");");
            AppendLine("return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(efCore.Context.GetTimeline(), ct);");
        });

        AppendLine();

        // Generate the timeline entry record
        XmlSummary($"Timeline entry for <see cref=\"{_model.TypeName}\"/> with LAG/LEAD columns.");
        AppendLine($"public sealed record {_model.TypeName}TimelineEntry");
        Block(() =>
        {
            foreach (var member in EntryMembers)
                AppendLine(member);
        });
    }

    /// <summary>The entry's members, written by this template and by <see cref="EngineTimelineTemplate" />: one entry type on every engine.</summary>
    internal static readonly string[] EntryMembers =
    [
        "public global::System.DateTimeOffset ValidFrom { get; init; }",
        "public global::System.DateTimeOffset? ValidTo { get; init; }",
        "public global::System.DateTimeOffset? PreviousValidTo { get; init; }",
        "public global::System.DateTimeOffset? NextValidFrom { get; init; }",
    ];

    /// <summary>The summary of <c>GetTimelineAsync</c>, the same on every engine.</summary>
    internal static string GetTimelineAsyncSummary(string typeName)
        => $"Gets the timeline of <see cref=\"{typeName}\"/> on every engine: each validity period with the end of the one before and the "
           + "start of the one after, partitioned by the parent, ordered by ValidFrom.";
}
