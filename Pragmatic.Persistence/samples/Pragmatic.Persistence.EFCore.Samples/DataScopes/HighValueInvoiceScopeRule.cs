using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.EFCore.Samples.DataScopes;

/// <summary>
///     A computed data-scope rule: invoices over 1,000 belong to the "high-value" scope.
///     <see cref="ScopeStrategy.Computed"/> means the expression is evaluated at query time
///     (always up-to-date, never materialized), OR-composed into the filter for users holding
///     <c>scope:high-value</c>.
/// </summary>
public sealed class HighValueInvoiceScopeRule : DataScopeRule<ScopedInvoice>
{
    public override string ScopeName => "high-value";

    public override ScopeStrategy Strategy => ScopeStrategy.Computed;

    public override Expression<Func<ScopedInvoice, bool>> ToExpression()
        => invoice => invoice.Amount > 1000m;
}
