using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary><c>PageAsync</c>: a page by specification, in a runtime ordering, with its total, inside the repository's filters.</summary>
internal sealed partial class RepositoryTemplate
{
    private void RenderPageAsync(string entityType)
    {
        XmlSummary($"Reads one page of the {_model.TypeName} entities matching a specification, in an ordering, with the total of the matches.");
        XmlParam("spec", "The specification to match.");
        XmlParam("ordering", "The keys to sort by.");
        XmlParam("page", "The page to read, 1-based.");
        XmlParam("pageSize", "How many rows a page holds.");
        XmlParam("ct", "Cancellation token.");

        var parameters = new List<MethodParameter>
        {
            new($"global::Pragmatic.Specification.ISpecification<{entityType}>", "spec"),
            new($"global::Pragmatic.Persistence.Query.Ordering<{entityType}>", "ordering"),
            new("int", "page"),
            new("int", "pageSize"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("PageAsync", () =>
        {
            // The same filters as FindAsync; the total counts the matches before the page is taken.
            AppendLine("var matches = ApplyFilters(Set).Where(spec.ToExpression());");
            AppendLine("var total = await matches.CountAsync(ct).ConfigureAwait(false);");
            AppendLine("var rows = await ordering.ApplyTo(matches).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);");
            AppendLine($"return global::Pragmatic.Persistence.Query.Results.PagedResult<{entityType}>.Success(rows, total, page, pageSize);");
        }, $"global::System.Threading.Tasks.Task<global::Pragmatic.Persistence.Query.Results.PagedResult<{entityType}>>", parameters,
            modifiers: new MethodModifiers { IsAsync = true });
    }
}
