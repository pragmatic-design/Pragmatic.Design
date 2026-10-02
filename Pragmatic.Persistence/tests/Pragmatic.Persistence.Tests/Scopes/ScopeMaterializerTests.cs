using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.Tests.Scopes;

/// <summary>
///     Tests for <see cref="ScopeMaterializer" />, which evaluates the registered
///     <see cref="DataScopeRule{T}" /> rules for an entity and grants/revokes scopes in its
///     <c>AccessScopes</c>. Rules are resolved from an <see cref="IServiceProvider" />; compiled
///     delegates are cached per rule type so the expression is not recompiled per entity.
/// </summary>
public class ScopeMaterializerTests
{
    private sealed class Invoice : IScopedEntity
    {
        public string Currency { get; set; } = "";
        public List<string> AccessScopes { get; } = [];
    }

    /// <summary>Materialized rule: invoices in EUR belong to the "eur" scope.</summary>
    private sealed class EurInvoiceRule : DataScopeRule<Invoice>
    {
        public int ToExpressionCallCount { get; private set; }

        public override string ScopeName => "eur";

        public override Expression<Func<Invoice, bool>> ToExpression()
        {
            ToExpressionCallCount++;
            return e => e.Currency == "EUR";
        }
    }

    /// <summary>Computed rule: must be skipped by materialization (query-time only).</summary>
    private sealed class ComputedUsdRule : DataScopeRule<Invoice>
    {
        public override string ScopeName => "usd";
        public override ScopeStrategy Strategy => ScopeStrategy.Computed;
        public override Expression<Func<Invoice, bool>> ToExpression() => e => e.Currency == "USD";
    }

    /// <summary>Minimal IServiceProvider returning the configured rule set for the entity type.</summary>
    private sealed class RuleProvider(IEnumerable<DataScopeRule<Invoice>> rules) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IEnumerable<DataScopeRule<Invoice>>) ? rules : null;
    }

    private static ScopeMaterializer MaterializerWith(params DataScopeRule<Invoice>[] rules)
        => new(new RuleProvider(rules));

    [Fact]
    public void Materialize_MatchingRule_AddsScopeId()
    {
        var materializer = MaterializerWith(new EurInvoiceRule());
        var invoice = new Invoice { Currency = "EUR" };

        materializer.Materialize(invoice);

        invoice.AccessScopes.Should().Contain("scope:eur");
    }

    [Fact]
    public void Materialize_NonMatchingRule_DoesNotAddScopeId()
    {
        var materializer = MaterializerWith(new EurInvoiceRule());
        var invoice = new Invoice { Currency = "USD" };

        materializer.Materialize(invoice);

        invoice.AccessScopes.Should().NotContain("scope:eur");
    }

    [Fact]
    public void Materialize_EntityNowOutOfScope_RemovesPreviouslyGrantedScope()
    {
        var materializer = MaterializerWith(new EurInvoiceRule());
        var invoice = new Invoice { Currency = "USD" };
        invoice.AccessScopes.Add("scope:eur"); // stale grant from a prior state

        materializer.Materialize(invoice);

        invoice.AccessScopes.Should().NotContain("scope:eur");
    }

    [Fact]
    public void Materialize_AlreadyGrantedScope_DoesNotDuplicate()
    {
        var materializer = MaterializerWith(new EurInvoiceRule());
        var invoice = new Invoice { Currency = "EUR" };
        invoice.AccessScopes.Add("scope:eur");

        materializer.Materialize(invoice);

        invoice.AccessScopes.Count(s => s == "scope:eur").Should().Be(1);
    }

    [Fact]
    public void Materialize_ComputedStrategyRule_IsSkipped()
    {
        // Computed rules are evaluated at query time, so materialization must not touch AccessScopes.
        var materializer = MaterializerWith(new ComputedUsdRule());
        var invoice = new Invoice { Currency = "USD" };

        materializer.Materialize(invoice);

        invoice.AccessScopes.Should().BeEmpty();
    }

    [Fact]
    public void Materialize_NoRulesRegistered_LeavesAccessScopesUntouched()
    {
        var materializer = MaterializerWith();
        var invoice = new Invoice { Currency = "EUR" };
        invoice.AccessScopes.Add("scope:existing");

        materializer.Materialize(invoice);

        invoice.AccessScopes.Should().ContainSingle().Which.Should().Be("scope:existing");
    }

    [Fact]
    public void Materialize_CompilesRuleExpressionAtMostOnce_AcrossManyEntities()
    {
        // Regression: ToExpression().Compile() must be cached per rule type, not run per entity.
        // The static delegate cache may have been primed by an earlier test (cache hit => 0 calls)
        // or this test may populate it (=> 1 call) — but never once-per-entity.
        var rule = new EurInvoiceRule();
        var materializer = MaterializerWith(rule);

        foreach (var i in Enumerable.Range(0, 50))
            materializer.Materialize(new Invoice { Currency = i % 2 == 0 ? "EUR" : "USD" });

        rule.ToExpressionCallCount.Should().BeLessThanOrEqualTo(1);
    }
}
