using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Comments;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The rules of a comment the documents engine persists, on MongoDB and Cosmos DB, as on the Sql engine (<c>CommentRulesTests</c> there): with
///     moderation only a visible comment is read, with internal notes an internal comment only by who holds view-internal. anna's review at RIVA
///     carries three comments: one visible, one pending, one internal.
/// </summary>
[Collection("document servers")]
public class CommentRulesTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<DocSession> Seeded(Store store, string[]? permissions = null)
    {
        var seeded = await DocumentRows.SeededAsync(servers, store);
        if (store == Store.Cosmos)
            await seeded.EnsureContainerAsync<ReviewComment>();
        var session = DocumentServers.As(seeded, DocumentRows.Anna(_toggle, permissions));
        Review review;
        using (_toggle.UseMode(FilterMode.Raw))
            review = (await session.Query<Review>().ToListAsync()).Single(r => r.Text == "anna@RIVA");
        await seeded.InsertManyAsync([
            new ReviewComment { Content = "visible", ReviewId = review.Id },
            new ReviewComment { Content = "pending", ReviewId = review.Id, Status = CommentStatus.PendingApproval },
            new ReviewComment { Content = "internal", ReviewId = review.Id, Visibility = CommentVisibility.Internal },
        ]);
        return session;
    }

    private static async Task<List<string>> Contents(DocSession session)
        => (await session.Query<ReviewComment>().ToListAsync()).Select(c => c.Content).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_pending_or_internal_comment_is_out_of_a_readers_reads(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        Assert.Equal(["visible"], await Contents(await Seeded(store)));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_moderation_rule_is_lifted_by_its_name_and_by_nothing_else(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store);
        using (_toggle.DisableQueryFilter(VisibilityFilterName.Of<ReviewComment.ModerationRule>()))
            Assert.Equal(["pending", "visible"], await Contents(session));
        // Admin lifts the permission filters, not a rule.
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["internal", "visible"], await Contents(session));
    }

    /// <summary>The list query [HasComments] generates, its invoker reading through the child's repository: the review's comments, inside the rules.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_list_query_reads_a_parents_comments_inside_the_rules(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store, [new ReviewComment.InternalVisibilityFilter().BypassPermission]);
        Review review;
        using (_toggle.UseMode(FilterMode.Raw))
            review = (await session.Query<Review>().ToListAsync()).Single(r => r.Text == "anna@RIVA");
        var services = new ServiceCollection()
            .AddScoped<Pragmatic.Persistence.Repository.IReadRepository<ReviewComment>>(_ => new ReviewComment.DocReadRepository(session))
            .BuildServiceProvider();

        var page = await new ListReviewCommentsQuery.Invoker(services).RunAsync(new ListReviewCommentsQuery { ReviewId = review.Id });

        Assert.Equal(["internal", "visible"], page.Value!.Items.Select(c => c.Content).Order(StringComparer.Ordinal));
        Assert.Equal(2, page.Value.TotalCount);
        // The DTO's Id is the comment's key (its shape's alias of PersistenceId), and its parent's key the review's.
        var stored = (await session.Query<ReviewComment>().ToListAsync()).ToDictionary(c => c.Content, c => c.PersistenceId);
        Assert.All(page.Value.Items, c => Assert.Equal(stored[c.Content], c.Id));
        Assert.All(page.Value.Items, c => Assert.Equal(review.Id, c.ReviewId));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_internal_comment_is_read_by_who_holds_view_internal(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store, [new ReviewComment.InternalVisibilityFilter().BypassPermission]);
        Assert.Equal(["internal", "visible"], await Contents(session));
    }
}
