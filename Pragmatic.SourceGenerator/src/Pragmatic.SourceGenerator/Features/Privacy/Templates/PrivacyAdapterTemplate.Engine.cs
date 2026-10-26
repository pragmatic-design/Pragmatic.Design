using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
///     The adapters of an entity the Sql or documents engine persists: the entity's repository and the route followed by key, one read per hop
///     (owner's decision of 2026-10-26: one body for both engines, as a document keeps a relation as its key and has no navigation to follow).
///     The subject's keys are read first, then each hop's rows whose foreign key is among the keys of the one before, then the entity's.
/// </summary>
/// <remarks>
///     Past the soft delete and no other filter, as EF Core's <c>IgnoreQueryFilters("SoftDelete")</c>: an engine lifts it when both its halves
///     are lifted, each soft-deleted entity's <c>SoftDeleteFilter</c> and the name. The tenant stays on.
/// </remarks>
internal abstract partial class PrivacyAdapterTemplate
{
    private const string Repository = "global::Pragmatic.Persistence.Repository";

    /// <summary>The entity's repository, the route's, the filter toggle, the subject registry and, to write, the boundary's unit of work.</summary>
    private void RenderEngineDependencies(string typeName, bool writes)
    {
        var rows = writes ? $"{Repository}.IRepository<global::{Model.FullTypeName}>" : $"{Repository}.IReadRepository<global::{Model.FullTypeName}>";
        var parameters = new List<MethodParameter> { new(rows, "rows") };
        AppendLine($"private readonly {rows} _rows;");
        for (var i = 1; i <= Route.Hops.Count; i++)
        {
            var hop = $"{Repository}.IReadRepository<global::{Route.Hops[i - 1].ToFullTypeName}>";
            AppendLine($"private readonly {hop} _hop{i};");
            parameters.Add(new MethodParameter(hop, $"hop{i}"));
        }

        AppendLine("private readonly global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle _toggle;");
        parameters.Add(new MethodParameter("global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle", "toggle"));
        if (writes)
        {
            AppendLine($"private readonly {Repository}.IUnitOfWork _unitOfWork;");
            parameters.Add(new MethodParameter($"{Repository}.IUnitOfWork", "unitOfWork") { Attribute = KeyedServiceAttribute() });
        }

        AppendLine("private readonly global::Pragmatic.Privacy.ISubjectRegistry _subjects;");
        parameters.Add(new MethodParameter("global::Pragmatic.Privacy.ISubjectRegistry", "subjects"));
        AppendLine();

        XmlSummary($"Creates a new {typeName}.");
        Constructor(typeName, () =>
        {
            foreach (var parameter in parameters)
                AppendLine($"_{parameter.Name} = {parameter.Name} ?? throw new global::System.ArgumentNullException(nameof({parameter.Name}));");
        }, parameters);
    }

    /// <summary>
    ///     Emits <c>rows</c>: the entity's rows for the subject, the route followed by key, the soft delete lifted for the reads (and the save
    ///     after them). <paramref name="nothingToDo" /> when a hop finds no key.
    /// </summary>
    protected void RenderEngineRows(string nothingToDo)
    {
        Comment("Past the soft delete and no other filter: a soft-deleted row is still personal data held about the subject.");
        AppendLine("using var _softDeleteName = _toggle.DisableQueryFilter(\"SoftDelete\");");
        for (var i = 0; i < Route.SoftDeletedTypes.Count; i++)
            AppendLine($"using var _softDelete{i} = _toggle.Disable<global::{Route.SoftDeletedTypes[i]}.SoftDeleteFilter>();");
        AppendLine();

        var hops = Route.Hops;
        if (hops.Count == 0)
        {
            AppendLine($"var rows = await _rows.FindAsync({Spec(Model.FullTypeName)}.Where({RowsForSubject()}), ct).ConfigureAwait(false);");
            return;
        }

        // The subject's keys, typed as the last hop's foreign key, then each hop's keys typed as the one before it.
        var n = hops.Count;
        AppendLine($"var keys{n} = (await _hop{n}.FindAsync({Spec(hops[n - 1].ToFullTypeName)}.Where(s => s.{Route.IdentifierProperty} == {Match.Operand}), ct).ConfigureAwait(false))");
        AppendLine($"    .Select(s => ({hops[n - 1].ForeignKeyType})s.PersistenceId).ToList();");
        AppendLine($"if (keys{n}.Count == 0) return {nothingToDo};");
        for (var i = n - 1; i >= 1; i--)
        {
            AppendLine($"var keys{i} = (await _hop{i}.FindAsync({Spec(hops[i - 1].ToFullTypeName)}.Where(e => keys{i + 1}.Contains(e.{hops[i].ForeignKey})), ct).ConfigureAwait(false))");
            AppendLine($"    .Select(e => ({hops[i - 1].ForeignKeyType})e.PersistenceId).ToList();");
            AppendLine($"if (keys{i}.Count == 0) return {nothingToDo};");
        }

        AppendLine($"var rows = await _rows.FindAsync({Spec(Model.FullTypeName)}.Where(row => keys1.Contains(row.{hops[0].ForeignKey})), ct).ConfigureAwait(false);");
    }

    private static string Spec(string fullTypeName) => $"global::Pragmatic.Specification.Spec<global::{fullTypeName}>";
}
