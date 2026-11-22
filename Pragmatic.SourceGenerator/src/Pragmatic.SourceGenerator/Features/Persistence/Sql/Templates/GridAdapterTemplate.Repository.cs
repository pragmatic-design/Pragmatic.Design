using System.Linq;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     The grid adapter's read through the entity's repository (#947): <c>LoadAsync(repository, options)</c>, the same call EF Core's adapter
///     has, so an endpoint that writes it compiles wherever the entity is persisted. The filter is <c>Parse</c>'s specification, the sort an
///     engine-neutral ordering whose members the repository maps to the keys it compiled for this adapter's sortable fields.
/// </summary>
internal sealed partial class GridAdapterTemplate
{
    private const string Repository = "global::Pragmatic.Persistence.Repository.IReadRepository";

    private string NeutralOrdering => $"global::Pragmatic.Persistence.Query.Ordering<{model.EntityType}>";

    private void RenderRepositoryLoad()
    {
        AppendLine("    /// <summary>The page a DevExpress grid asked for, read through the entity's repository with its total: the same call on every engine.</summary>");
        AppendLine($"    public static global::System.Threading.Tasks.Task<global::Pragmatic.Persistence.Query.Results.PagedResult<{model.EntityType}>> LoadAsync({Repository}<{model.EntityType}> repository, {Adapters}.DevExpressLoadOptions options, global::System.Threading.CancellationToken ct = default)");
        AppendLine("    {");
        AppendLine("        global::Pragmatic.Ensure.Ensure.ThrowIfNull(repository);");
        AppendLine("        global::Pragmatic.Ensure.Ensure.ThrowIfNull(options);");
        AppendLine("        var terms = 0;");
        AppendLine($"        var specification = options.Filter is {{ Count: > 0 }} filter ? Parse(filter, ref terms) : {Spec}.True;");
        AppendLine("        var ordering = PageOrdering(options.Sort, ref terms);");
        AppendLine("        var (page, size) = PageOf(options);");
        AppendLine("        return repository.PageAsync(specification, ordering, page, size, ct);");
        AppendLine("    }");
        AppendLine();
        AppendLine("    /// <summary>The sort as an ordering every engine reads: a column the adapter does not sort on is passed over, as LoadAsync's sort passes it.</summary>");
        AppendLine($"    private static {NeutralOrdering} PageOrdering(global::System.Collections.Generic.IReadOnlyList<{Adapters}.DevExpressSortDescriptor>? sort, ref int terms)");
        AppendLine("    {");
        AppendLine($"        var ordering = {NeutralOrdering}.Unordered;");
        AppendLine("        if (sort is null)");
        AppendLine("            return ordering;");
        AppendLine("        terms = CountedTerms(terms + sort.Count);");
        AppendLine("        foreach (var descriptor in sort)");
        AppendLine("        {");
        AppendLine("            if (descriptor is null)");
        AppendLine("                throw Malformed();");
        AppendLine("            ordering = descriptor.Selector?.ToLowerInvariant() switch");
        AppendLine("            {");
        foreach (var field in model.Fields.Where(f => f.Sortable))
            AppendLine($"                \"{field.Json.ToLowerInvariant()}\" => ordering.Then(\"{field.Property}\", e => e.{field.Property}, descriptor.Desc),");
        AppendLine("                _ => ordering,");
        AppendLine("            };");
        AppendLine("        }");
        AppendLine();
        AppendLine("        return ordering;");
        AppendLine("    }");
        AppendLine();
        AppendLine("    /// <summary>Skip and Take as a page: anything else is refused, since the page around Skip holds rows the grid did not ask for.</summary>");
        AppendLine($"    private static (int Page, int Size) PageOf({Adapters}.DevExpressLoadOptions options)");
        AppendLine("    {");
        AppendLine("        var skip = options.Skip ?? 0;");
        AppendLine("        if (options.Take is not { } take || take <= 0 || skip < 0 || skip % take != 0)");
        AppendLine($"            throw new {Adapters}.GridFilterRejectedException(null, {Adapters}.GridFilterRejection.NotAPage);");
        AppendLine("        return ((skip / take) + 1, take);");
        AppendLine("    }");
        AppendLine();
    }
}
