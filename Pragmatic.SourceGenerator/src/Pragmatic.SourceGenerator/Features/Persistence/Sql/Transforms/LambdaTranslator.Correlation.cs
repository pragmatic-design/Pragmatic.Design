using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>A collection's rows correlated to their owner's: the link, and every filter the child's reads carry.</summary>
internal sealed partial class LambdaTranslator
{
    /// <summary>
    ///     The correlation of a child subquery to its parent row: <c>c1."FK" = t."Key"</c>, plus a derived type's fixed predicate under the
    ///     child's alias. Through a join table (<c>[ManyToMany]</c>) the link is joined under <c>c1_l</c> (<paramref name="linkJoin" />) and
    ///     the correlation is on its parent column.
    /// </summary>
    private string? Correlation(EntityModel owner, string ownerAlias, CollectionModel collection, string childAlias, out EntityModel? child, out string? linkJoin, SyntaxNode where)
    {
        linkJoin = null;
        child = _entities(collection.Child);
        if (child is null)
        {
            Fail(where, $"{collection.Child.Name} is not an [Entity] the generator could read");
            return null;
        }

        if (collection.LinkProblem(owner, child) is { } problem) // a composite key on either side: one column per key column
        {
            Fail(where, problem);
            return null;
        }

        var side = collection.IsLink ? childAlias + "_l" : childAlias;
        linkJoin = collection.LinkJoin(child, childAlias, side);
        var text = collection.Matches(owner, ownerAlias, child, side); // every key column of a composite parent
        if (child.RowPredicate is { } fixedPredicate)
            text += " AND " + fixedPredicate.Replace("t.\"", childAlias + ".\""); // a derived type's rows, a [SoftDelete] child's live ones, a Pragmatic child's families
        if (child.IsPragmatic && child.Rules.Count > 0)
        {
            // A Pragmatic child's rules too: fixed predicates on its row (PRAG3030 refuses the others), so they need no parameter here.
            if (SqlInterceptorGenerator.RulesOn(child, _entities, _model.Compilation, childAlias, out var ruleProblem) is not { } rules)
            {
                Fail(where, ruleProblem ?? "the child's visibility rules are outside the translated subset");
                return null;
            }

            text += " AND " + rules;
        }

        return text;
    }
}
