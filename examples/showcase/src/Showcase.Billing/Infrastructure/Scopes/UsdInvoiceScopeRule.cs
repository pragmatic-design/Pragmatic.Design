using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;

namespace Showcase.Billing.Infrastructure.Scopes;

/// <summary>
///     Scope rule: invoices with USD currency belong to the "billing-us" scope.
///     Demonstrates multiple <see cref="DataScopeRule{T}"/> per entity type.
/// </summary>
public sealed class UsdInvoiceScopeRule : DataScopeRule<Invoice>
{
    public override string ScopeName => "billing-us";

    public override ScopeStrategy Strategy => ScopeStrategy.Materialized;

    public override Expression<Func<Invoice, bool>> ToExpression()
        => invoice => invoice.Currency == "USD";
}
