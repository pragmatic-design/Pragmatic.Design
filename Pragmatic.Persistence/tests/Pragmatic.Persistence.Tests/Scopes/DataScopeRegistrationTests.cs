using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Scopes;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.Scopes;

/// <summary>
///     <see cref="DataScopeServiceExtensions.AddDataScopeRule{TRule,T}" /> has to leave a filter
///     behind that the pipeline can actually resolve.
/// </summary>
/// <remarks>
///     Not with <c>TryAddScoped&lt;IQueryFilter, …&gt;</c>. <c>IQueryFilter</c> is resolved as a
///     collection, and <c>TryAdd</c> asks whether <em>any</em> registration of the service type
///     exists — soft-delete and tenant filters always get there first, so the line would be a no-op
///     and the computed rule would silently never filter.
///     <para>
///         A test that resolves the rule directly cannot see that. The registration is the part that
///         can fail, so the registration is what this asserts — with another filter registered first,
///         which is the condition that triggers it.
///     </para>
/// </remarks>
public class DataScopeRegistrationTests
{
    private sealed class Invoice : IScopedEntity
    {
        public string Currency { get; set; } = "";
        public List<string> AccessScopes { get; } = [];
    }

    private sealed class EurInvoiceRule : DataScopeRule<Invoice>
    {
        public override string ScopeName => "eur";
        public override Expression<Func<Invoice, bool>> ToExpression() => i => i.Currency == "EUR";
    }

    private sealed class UsdInvoiceRule : DataScopeRule<Invoice>
    {
        public override string ScopeName => "usd";
        public override Expression<Func<Invoice, bool>> ToExpression() => i => i.Currency == "USD";
    }

    /// <summary>A stand-in for the soft-delete or tenant filter that is always registered first.</summary>
    private sealed class SomeOtherFilter : IQueryFilter;

    [Fact]
    public void AddDataScopeRule_WithAnotherFilterAlreadyRegistered_StillRegistersTheComputedFilter()
    {
        var services = new ServiceCollection();
        services.AddScoped<IQueryFilter, SomeOtherFilter>();

        services.AddDataScopeRule<EurInvoiceRule, Invoice>();

        // The descriptors, not a built provider: this project has no DI container reference, and the
        // registration is precisely what went missing.
        services.Should().Contain(
            d => d.ServiceType == typeof(IQueryFilter)
                 && d.ImplementationType == typeof(ComputedScopeFilter<Invoice>),
            "the computed filter is what applies the rule — without it the rule is inert");
    }

    [Fact]
    public void AddDataScopeRule_CalledTwiceForTheSameEntity_RegistersTheFilterOnce()
    {
        var services = new ServiceCollection();

        services.AddDataScopeRule<EurInvoiceRule, Invoice>();
        services.AddDataScopeRule<UsdInvoiceRule, Invoice>();

        services.Count(d => d.ServiceType == typeof(IQueryFilter)
                            && d.ImplementationType == typeof(ComputedScopeFilter<Invoice>))
            .Should().Be(1, "two rules on one entity share one filter — the guard still has to hold");
    }
}
