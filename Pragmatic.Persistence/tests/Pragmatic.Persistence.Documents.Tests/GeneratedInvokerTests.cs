using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's operations over the entities, their invokers generated as an application's are, on MongoDB and Cosmos DB: the specification
///     an invoker writes (<c>[RequireExists]</c>'s <c>Spec&lt;Review&gt;.Where</c>, a leaf in generated code) is compiled by the documents engine
///     and answered inside the caller's filters.
/// </summary>
[Collection("document servers")]
public class GeneratedInvokerTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task RequireExists_answers_inside_the_callers_filters(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        List<Review> all;
        using (_toggle.UseMode(FilterMode.Raw))
            all = await DocumentServers.As(seeded, DocumentRows.Anna(_toggle)).Query<Review>().ToListAsync();
        var invoker = new CheckReviewAction.Invoker(new Review.DocReadRepository(DocumentServers.As(seeded, DocumentRows.Anna(_toggle))),
            new ServiceCollection().BuildServiceProvider());

        Assert.True((await invoker.InvokeAsync(new CheckReviewAction { Id = all.Single(r => r.Text == "anna@RIVA").Id })).IsSuccess);
        Assert.IsType<NotFoundError>((await invoker.InvokeAsync(new CheckReviewAction { Id = all.Single(r => r.Text == "bruno@RIVA").Id })).Error); // not hers
        Assert.IsType<NotFoundError>((await invoker.InvokeAsync(new CheckReviewAction { Id = Guid.NewGuid() })).Error);
    }
}
