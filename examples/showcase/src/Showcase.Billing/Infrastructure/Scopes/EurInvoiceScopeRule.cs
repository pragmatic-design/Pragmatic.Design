using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;

namespace Showcase.Billing.Infrastructure.Scopes;

/// <summary>
///     Scope rule: invoices with EUR currency belong to the "billing-eu" scope.
///     Demonstrates <see cref="DataScopeRule{T}"/> with materialized strategy.
///     Users/groups with "scope:billing-eu" in their AccessScopes see EUR invoices.
/// </summary>
public sealed class EurInvoiceScopeRule : DataScopeRule<Invoice>
{
    public override string ScopeName => "billing-eu";

    public override ScopeStrategy Strategy => ScopeStrategy.Materialized;

    public override Expression<Func<Invoice, bool>> ToExpression()
        => invoice => invoice.Currency == "EUR";
}
