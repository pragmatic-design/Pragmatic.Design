using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The Include paths: the hops of a lambda (member accesses, <c>Then</c> below a collection), a collection's filter, orderings and Take per parent.</summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>
    ///     The hops of an Include call, root first: the path of its lambda, member accesses through <c>!</c> and parentheses
    ///     (<c>l => l.Order!.Customer</c>: to-one hops) and <c>Then</c> steps below a collection or a navigation
    ///     (<c>c => c.Orders.Then(o => o.Lines)</c>). Null with the reason when a hop is not a navigation or collection.
    /// </summary>
    internal static List<IncludeHop>? IncludeHops(InvocationExpressionSyntax invocation, SemanticModel model, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, out string? error)
    {
        error = null;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 || arguments[0].Expression is not LambdaExpressionSyntax lambda || !TryParameter(model, lambda, out var parameter))
        {
            error = "Include takes one lambda with one parameter: a navigation path (o => o.Customer, l => l.Order!.Customer, c => c.Orders.Then(o => o.Lines))";
            return null;
        }

        var path = new List<PathStep>();
        if (!PathNames(lambda.ExpressionBody, parameter, model, path) || path.Count == 0)
        {
            error = $"'{lambda}' is not a navigation path on the lambda parameter (o => o.Customer, l => l.Order!.Customer, c => c.Orders.Then(o => o.Lines), c => c.Orders.Where(o => !o.IsDeleted))";
            return null;
        }

        var hops = new List<IncludeHop>();
        var current = entity;
        foreach (var step in path)
        {
            var name = step.Name;
            if (current.Navigation(name) is { } navigation)
            {
                var target = entities(navigation.Target);
                if (target is null)
                {
                    error = $"'{navigation.Target.Name}' is not an [Entity] the generator can read";
                    return null;
                }

                if (target.HasCompositeKey && navigation.ForeignKeys.Length != target.Keys.Count)
                {
                    error = $"'{target.Type.Name}' has a composite key ({target.KeyList}): the navigation joins on one foreign key per key column ({name}{{KeyProperty}})";
                    return null;
                }

                if (step.Filter is not null)
                {
                    error = $"'{name}' is a to-one navigation: a Where filters a collection";
                    return null;
                }

                hops.Add(new IncludeHop(name, target, navigation, null));
                current = target;
            }
            else if (current.Collection(name) is { } collection)
            {
                var child = entities(collection.Child);
                if (child is null)
                {
                    error = $"'{collection.Child.Name}' is not an [Entity] the generator can read";
                    return null;
                }

                if (collection.LinkProblem(current, child) is { } problem) // a composite key on either side: one column per key column
                {
                    error = problem;
                    return null;
                }

                if (step.Filter is { } filter && !FilterTranslates(filter, child, entities, model, out error))
                    return null;
                var orderings = OrderingsTranslate(step, child, entities, model, out error);
                if (orderings is null)
                    return null;
                if (step.Take is { } take)
                {
                    // A Take per parent: a window over the ordering, partitioned by the foreign key; the join table of a many-to-many side would have to go inside it.
                    if (take <= 0 || orderings.Count == 0 || collection.IsLink)
                    {
                        error = take <= 0 ? "Take in an Include takes a positive constant"
                            : orderings.Count == 0 ? $"'{name}' is taken per parent without an order: Take follows OrderBy in an Include (c.Orders.OrderBy(o => o.CreatedAt).Take(3))"
                            : $"'{name}' is a many-to-many side: a Take per parent goes through the join table, which is not written inside the window; load the rows and take in memory";
                        return null;
                    }
                }

                hops.Add(new IncludeHop(name, child, null, collection, step.Filter, model, orderings, step.Take));
                current = child;
            }
            else
            {
                error = $"'{name}' is not a navigation or a collection of {current.Type.Name}";
                return null;
            }
        }

        return hops;
    }

    /// <summary>A collection filter translates when it is a predicate over the child's columns and constants: the child plan is shared by every call, so no value of the caller can be in it.</summary>
    private static bool FilterTranslates(LambdaExpressionSyntax filter, EntityModel child, Func<INamedTypeSymbol, EntityModel?> entities, SemanticModel model, out string? error)
    {
        error = null;
        if (!TryParameter(model, filter, out var parameter) || filter.ExpressionBody is null)
        {
            error = "an Include filter is a lambda with one parameter and an expression body: c.Orders.Where(o => !o.IsDeleted)";
            return false;
        }

        var translator = new LambdaTranslator(model, entities, child, parameter, inline: true);
        if (translator.Predicate(filter.ExpressionBody) is not null)
            return true;
        error = translator.Error is { } why && why.Contains("value from outside the lambda")
            ? $"'{translator.ErrorExpression}' in the Include filter is a value of the caller: an Include filter compares columns with constants only (the child query is shared by every call); filter the parent chain, or the loaded rows"
            : $"'{translator.ErrorExpression}' in the Include filter cannot be translated: {translator.Error}";
        return false;
    }

    /// <summary>
    ///     The orderings of a collection step as SQL fragments over the child's alias (<c>t."CreatedAt" DESC</c>): columns and constants only (the
    ///     child plan is shared by every call), no navigation. An empty list without orderings; null with the reason.
    /// </summary>
    private static List<string>? OrderingsTranslate(PathStep step, EntityModel child, Func<INamedTypeSymbol, EntityModel?> entities, SemanticModel model, out string? error)
    {
        error = null;
        var orderings = new List<string>();
        foreach (var (lambda, descending) in step.Orderings)
        {
            if (!TryParameter(model, lambda, out var parameter) || lambda.ExpressionBody is null)
            {
                error = "an ordering in an Include is a lambda with one parameter selecting a column: c.Orders.OrderBy(o => o.CreatedAt)";
                return null;
            }

            var translator = new LambdaTranslator(model, entities, child, parameter, inline: true) { InlineConstants = true };
            var column = translator.ColumnOf(lambda.ExpressionBody);
            if (column is null || translator.ParameterPaths.Count > 0 || translator.Joins.Count > 0 || translator.DialectPieces.Count > 0)
            {
                error = column is null ? $"'{translator.ErrorExpression ?? lambda.ToString()}' in the Include ordering cannot be translated: {translator.Error}"
                    : $"'{lambda}' in the Include ordering reads a value of the caller or a navigation: an Include orders the children by their own columns";
                return null;
            }

            orderings.Add(column + (descending ? " DESC" : ""));
        }

        return orderings;
    }

    /// <summary>One step of an Include path: a property name and, for a collection, the filter (<c>Where</c>), the orderings (<c>OrderBy</c>, <c>ThenBy</c>) and the <c>Take</c> per parent written on it.</summary>
    private sealed class PathStep(string name)
    {
        public string Name { get; } = name;

        public LambdaExpressionSyntax? Filter { get; set; }

        public List<(LambdaExpressionSyntax Lambda, bool Descending)> Orderings { get; } = [];

        public int? Take { get; set; }
    }

    /// <summary>
    ///     The steps an Include path walks from its parameter, root first: member accesses through <c>!</c> and parentheses
    ///     (<c>l => l.Order!.Customer</c> → Order, Customer), a <c>SqlPath.Then</c> call as what its receiver reaches followed
    ///     by the path of its lambda from that lambda's own parameter (<c>c.Orders.Then(o => o.Lines)</c> → Orders, Lines), and
    ///     LINQ's <c>Where</c> on a collection as that collection's filter (<c>c.Orders.Where(o => !o.IsDeleted)</c>). False
    ///     when the expression is something else.
    /// </summary>
    private static bool PathNames(ExpressionSyntax? node, IParameterSymbol parameter, SemanticModel model, List<PathStep> steps)
    {
        var own = new List<string>();
        while (true)
        {
            switch (node)
            {
                case ParenthesizedExpressionSyntax paren:
                    node = paren.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                    node = suppress.Operand;
                    continue;
                case MemberAccessExpressionSyntax member:
                    own.Insert(0, member.Name.Identifier.ValueText);
                    node = member.Expression;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax receiver } then when IsThen(then, model) && then.ArgumentList.Arguments.Count == 1 && then.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax below && TryParameter(model, below, out var belowParameter):
                    if (!PathNames(receiver.Expression, parameter, model, steps) || !PathNames(below.ExpressionBody, belowParameter, model, steps))
                        return false;
                    steps.AddRange(own.Select(o => new PathStep(o)));
                    return true;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax receiver } call when CollectionOperator(call, model) is { } operation:
                {
                    // The operator belongs to the collection the receiver reaches (the ones before it applied first); a member read off the sequence is not a path.
                    if (own.Count > 0 || !PathNames(receiver.Expression, parameter, model, steps) || steps.Count == 0)
                        return false;
                    var last = steps[steps.Count - 1];
                    var argument = call.ArgumentList.Arguments[0].Expression;
                    switch (operation)
                    {
                        case "Where" when argument is LambdaExpressionSyntax filter && last.Filter is null:
                            last.Filter = filter;
                            return true;
                        case "OrderBy" or "OrderByDescending" when argument is LambdaExpressionSyntax key:
                            last.Orderings.Clear();
                            last.Orderings.Add((key, operation == "OrderByDescending"));
                            return true;
                        case "ThenBy" or "ThenByDescending" when argument is LambdaExpressionSyntax key && last.Orderings.Count > 0:
                            last.Orderings.Add((key, operation == "ThenByDescending"));
                            return true;
                        case "Take":
                            var constant = model.GetConstantValue(argument);
                            last.Take = constant.HasValue && constant.Value is int n ? n : 0; // a non-constant is reported as not positive
                            return true;
                        default:
                            return false;
                    }
                }

                case IdentifierNameSyntax root:
                    if (model.GetSymbolInfo(root).Symbol is not IParameterSymbol p || !SymbolEqualityComparer.Default.Equals(p, parameter))
                        return false;
                    steps.AddRange(own.Select(o => new PathStep(o)));
                    return true;
                default:
                    return false;
            }
        }
    }

    private static bool IsThen(InvocationExpressionSyntax invocation, SemanticModel model)
        => model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Then" } method && method.ContainingType.ToDisplayString() == "Pragmatic.Persistence.Sql.SqlPath";

    /// <summary>
    ///     LINQ's operators an Include path takes on a collection, as extensions on it with one argument: <c>Where</c> with a one-parameter
    ///     predicate (not the indexed overload), <c>OrderBy</c>/<c>ThenBy</c> and their descending forms with a key selector (no comparer),
    ///     <c>Take</c> with a count. The operator's name, or null for anything else.
    /// </summary>
    private static string? CollectionOperator(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.ArgumentList.Arguments.Count != 1 || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Parameters.Length: 1 } method || method.ContainingType.ToDisplayString() != "System.Linq.Enumerable")
            return null;
        return method.Name switch
        {
            "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" when method.Parameters[0].Type is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 2 } => method.Name,
            "Take" when method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 => method.Name,
            _ => null,
        };
    }
}
