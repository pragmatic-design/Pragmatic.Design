using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates <c>GetDescendantsBy{Navigation}()</c> and <c>GetAncestorsBy{Navigation}()</c> extension
///     methods, one pair per tree, using a recursive CTE.
/// </summary>
/// <remarks>
///     The navigation name is always in the method name, even with a single tree: an entity that grows
///     a second tree would otherwise change the shape of the first one's callers.
/// </remarks>
internal sealed class HierarchyQueryTemplate : CSharpTemplate
{
    private readonly HierarchyModel _model;

    public HierarchyQueryTemplate(HierarchyModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} Hierarchy CTE from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GenerateHierarchy] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "HierarchyQuery", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");

        AppendNamespace(_model.Namespace);
        AppendLine();

        var className = NamingHelper.AppendSuffix(_model.TypeName, "HierarchyExtensions");

        XmlSummary($"Generated hierarchy query extensions for <see cref=\"{_model.TypeName}\"/>.");

        Class(className, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        var first = true;
        foreach (var tree in _model.Trees)
        {
            if (!first)
                AppendLine();
            first = false;
            RenderTree(tree);
        }
    }

    private void RenderTree(HierarchyTreeModel tree)
    {
        var entityType = $"global::{_model.FullTypeName}";
        var idType = _model.IdType;

        // The SQL dialect (WITH vs WITH RECURSIVE, bracket vs double-quote identifiers) is
        // provider-specific and only known at runtime. Delegate SQL construction to the runtime
        // HierarchyCteSql helper. Table/column names are resolved from the EF model (not a naive plural
        // of the type name), honouring [Table]/[Column]/irregular plurals.
        var parentFk = tree.ParentForeignKey;
        var descendants = "GetDescendantsBy" + tree.NavigationName;
        var ancestors = "GetAncestorsBy" + tree.NavigationName;
        const string helper = "global::Pragmatic.Persistence.EFCore.Query.HierarchyCteSql";
        const string direction = "global::Pragmatic.Persistence.EFCore.Query.HierarchyCteSql.Direction";

        XmlSummary($"Gets all descendants of the specified <see cref=\"{_model.TypeName}\"/> along <c>{tree.NavigationName}</c>, using a provider-aware recursive CTE.");
        AppendLine($"public static global::System.Linq.IQueryable<{entityType}> {descendants}(this global::Microsoft.EntityFrameworkCore.DbContext dbContext, {idType} rootId)");
        Block(() =>
        {
            RenderModelResolution(entityType, parentFk);
            AppendLine($"var sqlText = {helper}.Build(dbContext.Database, __table, __id, __parent, {direction}.Descendants, excludeRoot: true);");
            AppendLine($"var sql = global::System.Runtime.CompilerServices.FormattableStringFactory.Create(sqlText, rootId);");
            AppendLine($"return global::Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.FromSql(dbContext.Set<{entityType}>(), sql);");
        });

        AppendLine();

        XmlSummary($"Gets all ancestors of the specified <see cref=\"{_model.TypeName}\"/> along <c>{tree.NavigationName}</c>, using a provider-aware recursive CTE.");
        AppendLine($"public static global::System.Linq.IQueryable<{entityType}> {ancestors}(this global::Microsoft.EntityFrameworkCore.DbContext dbContext, {idType} childId)");
        Block(() =>
        {
            RenderModelResolution(entityType, parentFk);
            AppendLine($"var sqlText = {helper}.Build(dbContext.Database, __table, __id, __parent, {direction}.Ancestors, excludeRoot: true);");
            AppendLine($"var sql = global::System.Runtime.CompilerServices.FormattableStringFactory.Create(sqlText, childId);");
            AppendLine($"return global::Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.FromSql(dbContext.Set<{entityType}>(), sql);");
        });
    }

    private void RenderModelResolution(string entityType, string parentFk)
    {
        AppendLine($"var __entityType = dbContext.Model.FindEntityType(typeof({entityType}))");
        AppendLine($"    ?? throw new global::System.InvalidOperationException(\"Entity '{_model.TypeName}' is not mapped in this DbContext; hierarchy queries are unavailable.\");");
        AppendLine("var __table = __entityType.GetTableName()");
        AppendLine($"    ?? throw new global::System.InvalidOperationException(\"Entity '{_model.TypeName}' has no table mapping; hierarchy queries are unavailable.\");");
        AppendLine("var __id = __entityType.FindProperty(\"PersistenceId\")?.GetColumnName() ?? \"PersistenceId\";");
        AppendLine($"var __parent = __entityType.FindProperty(\"{parentFk}\")?.GetColumnName() ?? \"{parentFk}\";");
    }
}
