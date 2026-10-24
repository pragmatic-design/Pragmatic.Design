using Pragmatic.Authorization;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using PragmaticDocumentSamples.Callers;
using PragmaticDocumentSamples.Entities;
using PragmaticDocumentSamples.Rules;

namespace PragmaticDocumentSamples.Flows;

/// <summary>
///     The other filter families on both backends, on the rows <see cref="EntitySamples" /> wrote, as anna: the temporal filter (IncludeHistory
///     lifts it), a computed scope, a materialized one (its rule's tree interpreted: no code emitted), a trait's child visible where its parent
///     is (the parents' keys read first), a tree's descendants.
/// </summary>
internal static class FamilySamples
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession session, DocSession anna, QueryFilterToggle toggle)
    {
        var checks = new Checks(backend);
        if (session.Cosmos is not null)
        {
            await session.EnsureContainerAsync<StaffAssignment>();
            await session.EnsureContainerAsync<ReviewComment>();
            await session.EnsureContainerAsync<Category>();
        }

        var now = DateTimeOffset.UtcNow;
        await session.InsertManyAsync([new StaffAssignment { Person = "current", ValidFrom = now.AddDays(-1) }, new StaffAssignment { Person = "past", ValidFrom = now.AddDays(-9), ValidTo = now.AddDays(-2) }]);
        List<Review> reviews;
        using (toggle.UseMode(FilterMode.Raw))
            reviews = await anna.Query<Review>().ToListAsync();
        await session.InsertManyAsync(reviews.Select(r => { var c = new ReviewComment { Text = "on " + r.Text }; c.SetReviewId(r.Id); return c; }).ToList());
        var root = new Category { Name = "ROOT" };
        var child = new Category { Name = "CHILD" };
        child.SetParentId(root.Id);
        var leaf = new Category { Name = "LEAF" };
        leaf.SetParentId(child.Id);
        await session.InsertManyAsync([root, child, leaf]);

        checks.Check("Pragmatic families: the stretches active now", await anna.Query<StaffAssignment>().CountAsync() == 1);
        using (StaffAssignment.IncludeHistory(toggle))
            checks.Check("Pragmatic families: IncludeHistory, every stretch", await anna.Query<StaffAssignment>().CountAsync() == 2);
        var seaside = EntitySamples.Caller(session, new PragmaticDocFilters(new SampleUser("anna"), new TenantScope(), new SampleScopes("desk-north", ScopeIdentifiers.ForScope("seaside")), toggle, scopeRules: [new SeasideRule()]));
        checks.Check("Pragmatic families: a computed scope adds its documents", (await seaside.Query<Property>().ToListAsync()).Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(["MARE", "RIVA"]));
        var unitOfWork = new DocUnitOfWork(anna, currentUser: new SampleUser("anna"), tenant: new TenantScope(), scopeRules: [new AlpineRule()]);
        var alba = new Property { Name = "ALBA" };
        new Property.DocRepository(unitOfWork).Add(alba);
        await unitOfWork.SaveChangesAsync();
        checks.Check("Pragmatic families: a materialized scope on the saved document", alba.AccessScopes.SequenceEqual([ScopeIdentifiers.ForUser("anna"), ScopeIdentifiers.ForScope("alpine")]));
        checks.Check("Pragmatic families: a trait's child where its parent is", await anna.Query<ReviewComment>().CountAsync() == 2); // anna's two reviews
        var descendants = await Category.GetDescendantsByParentAsync(anna, root.Id);
        checks.Check("Pragmatic families: a tree's descendants", descendants.Select(c => c.Name).Order(StringComparer.Ordinal).SequenceEqual(["CHILD", "LEAF"]));
        return checks.Run;
    }
}
