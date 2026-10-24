using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>Computed at read time: the properties by the sea are the scope "seaside".</summary>
public sealed class SeasideRule : Pragmatic.Persistence.Scopes.DataScopeRule<Property>
{
    public override string ScopeName => "seaside";

    public override Pragmatic.Persistence.Scopes.ScopeStrategy Strategy => Pragmatic.Persistence.Scopes.ScopeStrategy.Computed;

    public override System.Linq.Expressions.Expression<Func<Property, bool>> ToExpression() => p => p.Name == "MARE";
}
