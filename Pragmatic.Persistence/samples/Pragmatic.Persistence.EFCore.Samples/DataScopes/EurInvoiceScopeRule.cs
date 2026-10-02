using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;

namespace Pragmatic.Persistence.EFCore.Samples.DataScopes;

/// <summary>
///     A materialized data-scope rule: invoices in EUR belong to the "eur-invoices" scope.
///     <see cref="ScopeStrategy.Materialized"/> means the token is written into
///     <c>AccessScopes</c> on create/update (fast, indexed queries).
/// </summary>
public sealed class EurInvoiceScopeRule : DataScopeRule<ScopedInvoice>
{
    public override string ScopeName => "eur-invoices";

    public override ScopeStrategy Strategy => ScopeStrategy.Materialized;

    public override Expression<Func<ScopedInvoice, bool>> ToExpression()
        => invoice => invoice.Currency == "EUR";
}
