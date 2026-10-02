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

        // Generate the timeline entry record
        XmlSummary($"Timeline entry for <see cref=\"{_model.TypeName}\"/> with LAG/LEAD columns.");
        AppendLine($"public sealed record {_model.TypeName}TimelineEntry");
        Block(() =>
        {
            AppendLine("public global::System.DateTimeOffset ValidFrom { get; init; }");
            AppendLine("public global::System.DateTimeOffset? ValidTo { get; init; }");
            AppendLine("public global::System.DateTimeOffset? PreviousValidTo { get; init; }");
            AppendLine("public global::System.DateTimeOffset? NextValidFrom { get; init; }");
        });
    }
}
