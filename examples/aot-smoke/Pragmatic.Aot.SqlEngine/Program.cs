using Microsoft.Data.Sqlite;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Pragmatic.Result.Database;
using Pragmatic.Specification.Specifications;
using Pragmatic.Aot.SqlEngine;

// Pragmatic's entities under the Sql engine, native: the families per caller, the modes, a lift by name, a join, a subquery, an include,
// a command. Every check prints; the exit code is the number that failed.
var failures = 0;
void Check(string what, object expected, object actual)
{
    var ok = Equals(expected, actual) || (expected is System.Collections.IEnumerable e && actual is System.Collections.IEnumerable a && e.Cast<object>().SequenceEqual(a.Cast<object>()));
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}: {Text(actual)}{(ok ? "" : " (expected " + Text(expected) + ")")}");
    failures += ok ? 0 : 1;
    static string Text(object value) => value is System.Collections.IEnumerable list and not string ? "[" + string.Join(", ", list.Cast<object>()) + "]" : value.ToString() ?? "";
}

using var tenant = TenantScope.BeginScope("acme");
var toggle = new QueryFilterToggle();
IQueryFilterToggle lifts = toggle;
var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
using (var create = connection.CreateCommand())
{
    create.CommandText = """
        CREATE TABLE "Properties" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "TenantId" TEXT NOT NULL, "AccessScopes" TEXT NOT NULL);
        CREATE TABLE "Reservations" ("PersistenceId" TEXT PRIMARY KEY, "CheckIn" TEXT NOT NULL, "IsDeleted" INTEGER NOT NULL, "DeletedAt" TEXT NULL,
            "DeletedBy" TEXT NULL, "PropertyId" TEXT NOT NULL, "OwnerId" TEXT NOT NULL);
        CREATE TABLE "Reviews" ("PersistenceId" TEXT PRIMARY KEY, "Text" TEXT NOT NULL, "Published" INTEGER NOT NULL, "PropertyId" TEXT NOT NULL, "OwnerId" TEXT NOT NULL);
        CREATE TABLE "Amenities" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL UNIQUE, "RowVersion" INTEGER NOT NULL);
        CREATE TABLE "StaffAssignments" ("PersistenceId" TEXT PRIMARY KEY, "Person" TEXT NOT NULL, "ValidFrom" TEXT NOT NULL, "ValidTo" TEXT NULL);
        CREATE TABLE "ReviewComments" ("PersistenceId" TEXT PRIMARY KEY, "Text" TEXT NOT NULL, "ReviewId" TEXT NOT NULL);
        CREATE TABLE "Categories" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "ParentId" TEXT NULL);
        """;
    create.ExecuteNonQuery();
}

using var session = new SqlSession(connection, SqlDialect.Sqlite) { Filters = new PragmaticSqlFilters(new SmokeUser("anna"), new TenantScope(), new SmokeScopes("desk-north"), toggle) };
var reviewIds = new Dictionary<string, Guid>();
using (toggle.UseMode(FilterMode.Raw))
{
    var riva = new Property { Name = "RIVA", TenantId = "acme" };
    riva.GrantScope("desk-north");
    var mare = new Property { Name = "MARE", TenantId = "acme" };
    mare.GrantScope("desk-south");
    await session.InsertAsync(riva);
    await session.InsertAsync(mare);
    foreach (var (owner, at) in new[] { ("anna", riva), ("bruno", riva), ("anna", mare) })
    {
        var stay = new Reservation { CheckIn = DateTimeOffset.UnixEpoch };
        stay.SetOwnerId(owner);
        stay.SetPropertyId(at.Id);
        await session.InsertAsync(stay);
        var review = new Review { Text = owner + "@" + at.Name, Published = owner == "anna" };
        review.SetOwnerId(owner);
        review.SetPropertyId(at.Id);
        await session.InsertAsync(review);
        reviewIds[review.Text] = review.Id;
    }
}

Check("properties of the caller", new[] { "RIVA" }, await session.Query<Property>().Select(p => p.Name).ToListAsync());
Check("reservations of the caller", 2, await session.Query<Reservation>().CountAsync());
Check("a join to a property the caller cannot see", new[] { "RIVA" }, await session.Query<Reservation>().Select(r => r.Property.Name).ToListAsync());
Check("properties with a review the caller may read", new[] { "MARE", "RIVA" },
    await session.Query<Property>().WithoutFilters().Where(p => p.Reviews.Any()).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());
var included = await session.Query<Property>().Include(p => p.Reviews).FirstOrDefaultAsync();
Check("an include's children", new[] { "anna@RIVA" }, included!.Reviews.Select(r => r.Text).ToList());
using (toggle.UseMode(FilterMode.Admin))
    Check("Admin: every owner, the tenant kept", 3, await session.Query<Reservation>().CountAsync());
using (lifts.DisableVisibilityRule<PublishedReviews>())
using (toggle.UseMode(FilterMode.Admin))
    Check("Admin and the rule lifted by name: every review", 3, await session.Query<Review>().CountAsync());
// The read surface: Pragmatic's repository contract, a declared query, a DevExpress grid with a filter as a request body holds it.
Pragmatic.Persistence.Repository.IReadRepository<Review> reviews = new Review.SqlReadRepository(session);
Check("the repository by key", "anna@RIVA", (await reviews.GetByIdAsync(reviewIds["anna@RIVA"]))?.Text ?? "none");
Check("the repository by key, a row the caller cannot see", "none", (await reviews.GetByIdAsync(reviewIds["bruno@RIVA"]))?.Text ?? "none");
Check("the repository by a specification", 2, await reviews.CountAsync(Pragmatic.Specification.Spec<Review>.Where(r => r.Text.StartsWith("anna"))));
var declared = await reviews.RunAsync(new SmokeReviewsQuery { Text = "RIVA" });
Check("a declared query", "[anna@RIVA] of 1", $"[{string.Join(", ", declared.Items.Select(r => r.Text))}] of {declared.TotalCount}");
using var gridFilter = System.Text.Json.JsonDocument.Parse("""["!", ["text", "contains", "MARE"]]""");
var grid = await SmokeReviewGrid.LoadAsync(session, new Pragmatic.Persistence.Query.Adapters.DevExpressLoadOptions
{
    Filter = gridFilter.RootElement.EnumerateArray().Select(e => (object?)e).ToList(), Sort = [new() { Selector = "text" }], RequireTotalCount = true,
});
Check("a grid's page", "[anna@RIVA] of 1", $"[{string.Join(", ", grid.Items.Select(r => r.Text))}] of {grid.TotalCount}");

// The write surface: Pragmatic's unit of work and repository, the phases of its EF Core interceptors, a stale writer, a broken rule.
var unitOfWork = new SqlUnitOfWork(session, currentUser: new SmokeUser("anna"), tenant: new TenantScope());
var neve = new Property { Name = "NEVE", TenantId = "other" };
new Property.SqlRepository(unitOfWork).Add(neve);
await unitOfWork.SaveChangesAsync();
Check("an added row: the caller's tenant and scope", "acme user:anna", $"{neve.TenantId} {string.Join(",", neve.AccessScopes)}");
Pragmatic.Persistence.Repository.IRepository<Amenity> amenities = new Amenity.SqlRepository(unitOfWork);
var wifi = new Amenity { Name = "WiFi" };
amenities.Add(wifi);
await unitOfWork.SaveChangesAsync();
var other = new SqlUnitOfWork(session, currentUser: new SmokeUser("anna"), tenant: new TenantScope());
var theirs = (await new Amenity.SqlRepository(other).GetByIdAsync(wifi.Id))!;
wifi.Name = "WiFi 6";
Check("a loaded row saved, its version advanced", "1 1", $"{await unitOfWork.SaveChangesAsync()} {wifi.RowVersion}");
theirs.Name = "Fibre";
Check("the second writer of a row", "CONCURRENCY_CONFLICT", await Refused(other));
amenities.Add(new Amenity { Name = "WiFi 6" });
Check("a duplicate", "DB_CONFLICT Name", await Refused(unitOfWork));

// Phase 2b: the temporal filter, a computed scope, a materialized one (its rule's tree interpreted: no code emitted), a trait's child, a tree.
using (toggle.UseMode(FilterMode.Raw))
{
    var now = DateTimeOffset.UtcNow;
    await session.InsertAsync(new StaffAssignment { Person = "current", ValidFrom = now.AddDays(-1) });
    await session.InsertAsync(new StaffAssignment { Person = "past", ValidFrom = now.AddDays(-9), ValidTo = now.AddDays(-2) });
    foreach (var (text, id) in reviewIds)
    {
        var comment = new ReviewComment { Text = "on " + text };
        comment.SetReviewId(id);
        await session.InsertAsync(comment);
    }

    var root = new Category { Name = "ROOT" };
    var child = new Category { Name = "CHILD" };
    child.SetParentId(root.Id);
    var leaf = new Category { Name = "LEAF" };
    leaf.SetParentId(child.Id);
    foreach (var node in new[] { root, child, leaf })
        await session.InsertAsync(node);
}

Check("the stretches active now", 1, await session.Query<StaffAssignment>().CountAsync());
using (StaffAssignment.IncludeHistory(lifts))
    Check("IncludeHistory: every stretch", 2, await session.Query<StaffAssignment>().CountAsync());
var seaside = new SqlSession(connection, SqlDialect.Sqlite)
{
    Filters = new PragmaticSqlFilters(new SmokeUser("anna"), new TenantScope(), new SmokeScopes("desk-north", ScopeIdentifiers.ForScope("seaside")), toggle, scopeRules: [new SeasideRule()]),
};
Check("a computed scope adds its rows", new[] { "MARE", "RIVA" }, await seaside.Query<Property>().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());
var materializing = new SqlUnitOfWork(session, currentUser: new SmokeUser("anna"), tenant: new TenantScope(), scopeRules: [new AlpineRule()]);
var alba = new Property { Name = "ALBA" };
new Property.SqlRepository(materializing).Add(alba);
await materializing.SaveChangesAsync();
Check("a materialized scope on the saved row", "user:anna scope:alpine", string.Join(" ", alba.AccessScopes));
Check("a trait's child where its parent is", 2, await session.Query<ReviewComment>().CountAsync());
var tree = (await session.Query<Category>().ToListAsync()).ToDictionary(c => c.Name);
Check("a tree's descendants", new[] { "CHILD", "LEAF" }, (await Category.GetDescendantsByParentAsync(session, tree["ROOT"].Id)).Select(c => c.Name).Order(StringComparer.Ordinal).ToList());

Check("a command on the caller's rows", 2,await session.Query<Review>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Text, "edited")));
Check("back to the caller's variant", new[] { "RIVA" }, await session.Query<Property>().Select(p => p.Name).ToListAsync());

// The structure of a composed specification, read from its public nodes: what the engine composes its compiled leaves along.
var publishedSpec = Pragmatic.Specification.Spec<Review>.Where(r => r.Published);
var shortSpec = Pragmatic.Specification.Spec<Review>.Where(r => r.Text == "short");
var composed = (publishedSpec & !shortSpec) | Pragmatic.Specification.Spec<Review>.True;
Check("a composed specification's nodes, natively", "Or And True Not same",
    composed is OrSpecification<Review> { Left: AndSpecification<Review> and, Right: TrueSpecification<Review> }
        ? $"Or And True {(and.Right is NotSpecification<Review> not ? "Not" : "?")} {(ReferenceEquals(and.Left, publishedSpec) && and.Right is NotSpecification<Review> { Inner: var inner } && ReferenceEquals(inner, shortSpec) ? "same" : "other")}"
        : composed.GetType().Name);

Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} checks failed");
return failures;

// The code of the rule a save broke (and the field of a duplicate), or "saved" when it went through.
static async Task<string> Refused(SqlUnitOfWork unitOfWork)
{
    try
    {
        await unitOfWork.SaveChangesAsync();
        return "saved";
    }
    catch (Pragmatic.Persistence.Repository.PersistenceRuleViolationException broken)
    {
        return broken.Error is DbConflictError { FieldName: { } field } ? broken.Error.Code + " " + field : broken.Error.Code;
    }
}
