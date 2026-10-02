using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.EFCore.Scopes;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Scopes;
using Xunit;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A registered <see cref="DataScopeRule{T}" /> is evaluated against the rows it is about.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It never was. <c>IScopeMaterializer</c> — the thing that turns a rule into a
///         <c>scope:{name}</c> on a row — was registered by an application and called by nothing but its
///         own unit tests, so a rule could be declared, accepted, and never once evaluated. Measured on
///         the Showcase: two <c>DataScopeRule&lt;Invoice&gt;</c> registered, and every invoice reaching
///         the database with an empty scope list.
///     </para>
///     <para>
///         The order against the creator's stamp is the correctness of this and is fixed in
///         <see cref="ScopeInterceptor" />, not in a registration sequence: the stamp runs first and only
///         when the list is empty, so materialising first would make "empty" never true again and would
///         silently leave every row without its creator's scope.
///     </para>
/// </remarks>
public class ADataScopeRuleReachesTheRowTests
{
    /// <summary>An inserted row carries the scope of every rule that matches it.</summary>
    [Fact]
    public async Task AnInsertedRow_CarriesTheScopeOfEveryRuleThatMatches()
    {
        await using var db = Context(currentUser: null);

        var invoice = new ScopedInvoice { Currency = "EUR" };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.AccessScopes.Should().Contain("scope:billing-eu");
        invoice.AccessScopes.Should().NotContain("scope:billing-usd");
    }

    /// <summary>
    ///     The creator's stamp and the rule compose: both are on the row.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the case the ordering exists for. The stamp writes only when the list is empty, so
    ///     if materialisation ran first the list would never be empty again and no row would ever carry
    ///     its creator's scope, in silence.
    /// </remarks>
    [Fact]
    public async Task AnInsertedRow_CarriesBothTheCreatorsScopeAndTheRules()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"));

        var invoice = new ScopedInvoice { Currency = "EUR" };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.AccessScopes.Should().Contain("user:alice");
        invoice.AccessScopes.Should().Contain("scope:billing-eu");
    }

    /// <summary>
    ///     A row whose data changes is re-materialised: it loses the scope it no longer belongs to.
    /// </summary>
    /// <remarks>
    ///     This is where materialisation differs from the stamp, and why it runs on updates too. A
    ///     materialised scope is a function of the row's data; leaving the old one behind would keep the
    ///     invoice visible to a department it left.
    /// </remarks>
    [Fact]
    public async Task AnUpdatedRow_LosesTheScopeItNoLongerBelongsTo()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"));

        var invoice = new ScopedInvoice { Currency = "EUR" };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.Currency = "USD";
        await db.SaveChangesAsync();

        invoice.AccessScopes.Should().Contain("scope:billing-usd");
        invoice.AccessScopes.Should().NotContain("scope:billing-eu");
    }

    /// <summary>
    ///     The control, and the pair that keeps the two mechanisms distinct: an updated row is
    ///     re-materialised and is <b>not</b> re-stamped.
    /// </summary>
    /// <remarks>
    ///     An implementation that re-ran both on update would hand the row to the last person who
    ///     touched it, and everyone who could see it before would lose it. The previous test and this one
    ///     only pass together.
    /// </remarks>
    [Fact]
    public async Task AnUpdatedRow_IsRematerialisedButNotRestamped()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"));

        var invoice = new ScopedInvoice { Currency = "EUR" };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.AccessScopes.Remove("user:alice");
        invoice.Currency = "USD";
        await db.SaveChangesAsync();

        invoice.AccessScopes.Should().Contain("scope:billing-usd", "the rule follows the data");
        invoice.AccessScopes.Should().NotContain("user:alice", "only an insert is attributed");
    }

    /// <summary>The second control: no rule registered, nothing materialised, and no failure.</summary>
    [Fact]
    public async Task WithNoRuleRegistered_TheRowCarriesOnlyTheStamp()
    {
        await using var db = Context(FakeCurrentUser.Authenticated("alice"), withRules: false);

        var invoice = new ScopedInvoice { Currency = "EUR" };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.AccessScopes.Should().BeEquivalentTo(["user:alice"]);
    }

    private static ScopeRuleTestContext Context(ICurrentUser? currentUser, bool withRules = true)
    {
        var services = new ServiceCollection();
        if (withRules)
        {
            services.AddSingleton<DataScopeRule<ScopedInvoice>, EurRule>();
            services.AddSingleton<DataScopeRule<ScopedInvoice>, UsdRule>();
        }

        var provider = services.BuildServiceProvider();
        var materializer = new ScopeMaterializer(provider);

        var options = new DbContextOptionsBuilder<ScopeRuleTestContext>()
            .UseInMemoryDatabase($"ScopeRuleDb_{Guid.NewGuid():N}")
            .AddInterceptors(new ScopeInterceptor(
                currentUser,
                [new ScopeMaterializationStep<ScopedInvoice>(materializer)]))
            .Options;

        var context = new ScopeRuleTestContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class EurRule : DataScopeRule<ScopedInvoice>
    {
        public override string ScopeName => "billing-eu";

        public override ScopeStrategy Strategy => ScopeStrategy.Materialized;

        public override Expression<Func<ScopedInvoice, bool>> ToExpression()
            => invoice => invoice.Currency == "EUR";
    }

    private sealed class UsdRule : DataScopeRule<ScopedInvoice>
    {
        public override string ScopeName => "billing-usd";

        public override ScopeStrategy Strategy => ScopeStrategy.Materialized;

        public override Expression<Func<ScopedInvoice, bool>> ToExpression()
            => invoice => invoice.Currency == "USD";
    }

    private sealed class ScopedInvoice : IScopedEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Currency { get; set; } = string.Empty;

        public List<string> AccessScopes { get; } = [];
    }

    private sealed class ScopeRuleTestContext(DbContextOptions<ScopeRuleTestContext> options)
        : DbContext(options)
    {
        public DbSet<ScopedInvoice> Invoices => Set<ScopedInvoice>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ScopedInvoice>(entity =>
            {
                entity.HasKey(i => i.Id);
                entity.PrimitiveCollection(i => i.AccessScopes);
            });
        }
    }
}
