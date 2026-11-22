namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The DevExpress read through the entity's repository (#947): <c>LoadAsync(repository, options)</c>, the call the Sql and documents
///     engines' adapters have too, so an endpoint that writes it compiles wherever the entity is persisted. The filter and the sort are the
///     ones <c>Apply</c> builds, as a specification and an engine-neutral ordering; the page is <c>Skip</c> and <c>Take</c>, read as one.
/// </summary>
internal sealed partial class GridAdapterTemplate
{
    private void RenderDevExpressLoad()
    {
        var entity = _model.EntityTypeFullName;
        var ordering = $"global::Pragmatic.Persistence.Query.Ordering<{entity}>";
        XmlSummary("The page a DevExpress grid asked for, read through the entity's repository with its total: the same call on every engine.");
        XmlParam("repository", "The entity's read repository.");
        XmlParam("options", "The DevExpress load options from frontend.");
        XmlParam("ct", "Cancellation token.");
        AppendLine($"public static global::System.Threading.Tasks.Task<global::Pragmatic.Persistence.Query.Results.PagedResult<{entity}>> LoadAsync(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Persistence.Repository.IReadRepository<{entity}> repository,");
        AppendLine("DevExpressLoadOptions options,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(repository);");
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(options);");
            AppendLine("var terms = 0;");
            AppendLine($"var specification = global::Pragmatic.Specification.Spec<{entity}>.True;");
            AppendLine("if (options.Filter != null && options.Filter.Count > 0)");
            Block(() =>
            {
                if (_model.SupportNestedFilters)
                {
                    AppendLine($"specification = global::Pragmatic.Specification.Spec<{entity}>.Where(ParseFilterExpression(options.Filter, ref terms));");
                }
                else
                {
                    AppendLine("// Without nested filters a filter is one clause; a group is refused, not ignored.");
                    AppendLine("if (options.Filter.Count != 3 || Unwrap(options.Filter[0]) is not string field) throw Malformed();");
                    AppendLine("terms = CountedTerms(terms + 1);");
                    AppendLine($"specification = global::Pragmatic.Specification.Spec<{entity}>.Where(CreateFieldFilter(field, Unwrap(options.Filter[1]) as string, Unwrap(options.Filter[2])));");
                }
            });
            AppendLine();
            AppendLine($"var ordering = {ordering}.Unordered;");
            AppendLine("if (options.Sort != null)");
            Block(() =>
            {
                AppendLine("terms = CountedTerms(terms + options.Sort.Count);");
                AppendLine("foreach (var sort in options.Sort)");
                Block(() =>
                {
                    AppendLine("// As Apply sorts: a null is malformed, an empty selector and a column the adapter does not sort on are passed over.");
                    AppendLine("if (sort is null) throw Malformed();");
                    AppendLine("if (string.IsNullOrEmpty(sort.Selector)) continue;");
                    AppendLine("ordering = sort.Selector.ToLowerInvariant() switch");
                    AppendLine("{");
                    IncreaseIndent();
                    foreach (var field in _model.Fields.Where(f => f.Sortable))
                        AppendLine($"\"{field.JsonField.ToLowerInvariant()}\" => ordering.Then(\"{field.PropertyPath}\", e => e.{field.PropertyPath}, sort.Desc),");
                    AppendLine("_ => ordering");
                    DecreaseIndent();
                    AppendLine("};");
                });
            });
            AppendLine();
            AppendLine("// Skip and Take as a page: anything else is refused, since the page around Skip holds rows the grid did not ask for.");
            AppendLine("var skip = options.Skip ?? 0;");
            AppendLine("if (options.Take is not { } take || take <= 0 || skip < 0 || skip % take != 0)");
            IncreaseIndent();
            AppendLine($"throw new {Adapters}.GridFilterRejectedException(null, {Adapters}.GridFilterRejection.NotAPage);");
            DecreaseIndent();
            AppendLine("return repository.PageAsync(specification, ordering, (skip / take) + 1, take, ct);");
        });
    }
}
