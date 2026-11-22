using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The grid filter's read through the entity's repository (#946): <c>ToOrdering()</c> and <c>LoadAsync(repository)</c>, the same text
///     on every engine, and on an engine the refused <c>Apply</c>.
/// </summary>
internal sealed partial class GridFilterApplyTemplate
{
    /// <summary>On an engine: the engine reads the declaration through <c>LoadAsync</c>, not an <c>IQueryable</c>.</summary>
    private void RenderRefusedApply()
    {
        var entity = $"global::{_model.EntityTypeFullName}";
        XmlSummary($"Not served on the {_engine!.Engine}: it reads no IQueryable. Call LoadAsync with the entity's repository.");
        AppendLine($"public IQueryable<{entity}> Apply(IQueryable<{entity}> query)");
        AppendLine($"    => throw new global::System.NotSupportedException(\"{_model.TypeName} runs on the {_engine.Engine}, which reads the grid filter through LoadAsync(IReadRepository), not an IQueryable.\");");
    }

    /// <summary>
    ///     <c>ToOrdering()</c>: the sorts <c>Apply</c> applies, in the same order and on the same condition (the sort property set), as an
    ///     ordering every engine reads.
    /// </summary>
    private void RenderToOrderingMethod()
    {
        var entity = $"global::{_model.EntityTypeFullName}";
        var ordering = $"global::Pragmatic.Persistence.Query.Ordering<{entity}>";
        XmlSummary("The sorts as an ordering every engine reads: the set ones, in the order Apply applies them.");
        Method("ToOrdering", () =>
        {
            AppendLine($"var ordering = {ordering}.Unordered;");
            foreach (var prop in _model.SortableProperties)
            {
                var path = prop.EffectivePropertyPath;
                AppendLine($"if (this.{prop.PropertyName} is {{ }} {prop.PropertyName}Direction)");
                AppendLine($"    ordering = ordering.Then(\"{path}\", e => e.{path}, {prop.PropertyName}Direction == SortDirection.Descending);");
            }

            Return("ordering");
        }, ordering);
    }

    /// <summary>
    ///     <c>LoadAsync(repository)</c>: the page read through the entity's repository, the same call on every engine, so an endpoint that
    ///     writes it compiles wherever the entity is persisted.
    /// </summary>
    private void RenderLoadAsyncMethod()
    {
        var entity = $"global::{_model.EntityTypeFullName}";
        XmlSummary("The page this grid filter asks for, read through the entity's repository with its total: the same call on every engine.");
        XmlParam("repository", "The entity's read repository.");
        XmlParam("ct", "Cancellation token.");
        var parameters = new List<MethodParameter>
        {
            new($"global::Pragmatic.Persistence.Repository.IReadRepository<{entity}>", "repository"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };
        Method("LoadAsync", () =>
        {
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(repository);");
            // The bounds Apply pages with.
            Return($"repository.PageAsync(ToSpecification(), ToOrdering(), Math.Max(1, this.{_model.PageProperty!.PropertyName}), Math.Max(1, this.{_model.PageSizeProperty!.PropertyName}), ct)");
        }, $"global::System.Threading.Tasks.Task<global::Pragmatic.Persistence.Query.Results.PagedResult<{entity}>>", parameters);
    }
}
