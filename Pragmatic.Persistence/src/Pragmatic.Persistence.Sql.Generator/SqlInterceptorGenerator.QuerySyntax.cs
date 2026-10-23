using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A query expression (<c>from t in session.Query&lt;T&gt;() join … select …</c>) over a <c>SqlQuery&lt;T&gt;</c>: the compiler binds its
///     clauses to the operators with no invocation in the source, and an interceptor needs one. Refused where it is written, at compile
///     time, instead of the operator's NotSupportedException at run time.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private const string QuerySyntaxReason = "a query expression binds to the operators with no invocation the generator can intercept; write it as the method chain (Where, Join, GroupJoin with SelectMany and DefaultIfEmpty, Select)";

    /// <summary>The query expression's location when one of its sources is a SqlQuery; null otherwise.</summary>
    private static Location? QuerySyntaxOverSql(QueryExpressionSyntax query, SemanticModel model, CancellationToken ct)
    {
        var sources = new[] { query.FromClause.Expression }
            .Concat(query.Body.Clauses.OfType<JoinClauseSyntax>().Select(j => j.InExpression))
            .Concat(query.Body.Clauses.OfType<FromClauseSyntax>().Select(f => f.Expression));
        foreach (var source in sources)
        {
            if (model.GetTypeInfo(source, ct).Type is INamedTypeSymbol { Name: "SqlQuery", Arity: 1, ContainingNamespace: { Name: "Sql", ContainingNamespace: { Name: "Persistence", ContainingNamespace.Name: "Pragmatic" } } })
                return query.GetLocation();
        }

        return null;
    }
}
