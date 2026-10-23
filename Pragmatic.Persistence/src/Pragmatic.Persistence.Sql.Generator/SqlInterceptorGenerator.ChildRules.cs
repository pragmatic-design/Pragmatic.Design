using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

public sealed partial class SqlInterceptorGenerator
{
    internal sealed partial class Projection
    {
        /// <summary>
        ///     The child's visibility rules over its row in the correlated subquery (<c>c</c>), their constants as literals (a projection's column is
        ///     text: no parameter to bind; a boolean as the <c>{bt:1}</c> token each dialect resolves). Null with a problem when a rule reads a
        ///     value of the moment, a navigation or another dialect-dependent piece.
        /// </summary>
        private static string? ChildRules(EntityModel child, Func<INamedTypeSymbol, EntityModel?> entities, Compilation? compilation, out string? problem)
            => RulesOn(child, entities, compilation, "c", out problem);
    }

    /// <summary>
    ///     An entity's visibility rules over its row under <paramref name="alias" />, their constants as literals: what a statement that binds
    ///     no parameter of its own can carry (a projection's column, a correlated subquery of a translator). A Pragmatic entity's rule is
    ///     marked as its own slot (lifted by its name). Null with a problem when a rule reads a value of the moment, a navigation or another
    ///     dialect-dependent piece.
    /// </summary>
    internal static string? RulesOn(EntityModel child, Func<INamedTypeSymbol, EntityModel?> entities, Compilation? compilation, string alias, out string? problem)
    {
        problem = null;
        var parts = new List<string>();
        foreach (var rule in child.Rules)
        {
            LambdaExpressionSyntax? lambda = null;
            SemanticModel? ruleModel = null;
            if (compilation is not null)
                lambda = RuleLambda(rule, compilation, out ruleModel);
            var parameterSyntax = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter,
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0],
                _ => null,
            };
            if (lambda is null || ruleModel is null || parameterSyntax is null || ruleModel.GetDeclaredSymbol(parameterSyntax) is not IParameterSymbol parameter)
            {
                problem = $"{child.Type.Name}'s rule {rule.Name}: its ToExpression must be an expression-bodied lambda in this compilation";
                return null;
            }

            var translator = new LambdaTranslator(ruleModel, entities, new EntityRow(child, alias), parameter) { InlineConstants = true };
            var predicate = translator.Predicate(Body(lambda));
            var pieces = translator.DialectPieces;
            if (predicate is null || translator.ParameterPaths.Count > 0 || translator.Joins.Count > 0 || pieces.Any(p => !(p.Sqlite == "1" && p.PostgreSql == "true" && p.SqlServer == "1")))
            {
                problem = $"{child.Type.Name}'s rule {rule.Name} reads a value of the moment, a navigation or a dialect's function, which a projection's column cannot bind: aggregate through the collection member, or in a [Query]";
                return null;
            }

            for (var i = 0; i < pieces.Count; i++)
                predicate = predicate.Replace("{d" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", "{bt:1}"); // the boolean true each dialect writes its way
            parts.Add(child.IsPragmatic ? child.MarkRule(rule, predicate) : "(" + predicate + ")");
        }

        return string.Join(" AND ", parts);
    }
}
