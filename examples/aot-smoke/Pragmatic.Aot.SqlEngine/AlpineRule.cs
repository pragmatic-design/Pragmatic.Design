using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>Written on the saved rows: the properties starting with "A" carry "scope:alpine".</summary>
public sealed class AlpineRule : Pragmatic.Persistence.Scopes.DataScopeRule<Property>
{
    public override string ScopeName => "alpine";

    public override System.Linq.Expressions.Expression<Func<Property, bool>> ToExpression() => p => p.Name.StartsWith("A");
}
