using System.Linq.Expressions;
using Pragmatic.Persistence.Scopes;
using PragmaticDocumentSamples.Entities;

namespace PragmaticDocumentSamples.Rules;

/// <summary>Written on the saved documents: the properties starting with "A" carry "scope:alpine".</summary>
public sealed class AlpineRule : DataScopeRule<Property>
{
    public override string ScopeName => "alpine";

    public override Expression<Func<Property, bool>> ToExpression() => p => p.Name.StartsWith("A");
}
