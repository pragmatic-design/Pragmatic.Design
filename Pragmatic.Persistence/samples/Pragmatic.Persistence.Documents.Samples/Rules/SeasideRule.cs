using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Rules;

/// <summary>Computed at read time: the properties by the sea are the scope "seaside".</summary>
public sealed class SeasideRule : DataScopeRule<Property>
{
    public override string ScopeName => "seaside";

    public override ScopeStrategy Strategy => ScopeStrategy.Computed;

    public override Expression<Func<Property, bool>> ToExpression() => p => p.Name == "MARE";
}
