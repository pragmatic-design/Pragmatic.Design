using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The result of a <c>LeftJoin</c>, <c>RightJoin</c> or <c>FullJoin</c>. The caller's result selector runs in memory, where a side that is
///     missing is null and <c>c.Count</c> throws; EF Core and the Sql engine read the same member as a column that is NULL. So the plan builds
///     the rows with a result of its own, the caller's anonymous object with each member of a side that may be missing read through a
///     null-conditional access (<c>(int?)c?.Count</c>). In the same assembly an anonymous object of the same members is the same type, so the
///     row is the caller's <c>TResult</c>, as a grouped Select's reader builds it.
/// </summary>
internal sealed partial class DocPlanner
{
    private const string OuterJoinShape = "(o, c) => new { o.Number, Name = c.Name, Count = (int?)c.Count }";

    private static readonly SymbolDisplayFormat NullableQualified = DocModel.Qualified.WithMiscellaneousOptions(
        DocModel.Qualified.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The result as a <c>Func&lt;object?, object?, object&gt;</c> over the two sides; null when it was refused, with the reason.</summary>
    private string? OuterJoinResult(DocStep step, ExpressionSyntax argument, INamedTypeSymbol innerType)
    {
        var outerOptional = step.Name is "RightJoin" or "FullJoin";
        var innerOptional = step.Name is "LeftJoin" or "FullJoin";
        if (argument is not ParenthesizedLambdaExpressionSyntax { Body: AnonymousObjectCreationExpressionSyntax creation } lambda || Parameters(lambda) is not { Count: 2 } parameters)
        {
            Fail(argument, $"the result of a {step.Name} is a new object of the two sides' fields: {OuterJoinShape}");
            return null;
        }

        var members = new List<string>();
        foreach (var initializer in creation.Initializers)
        {
            var expression = initializer.Expression;
            var cast = expression as CastExpressionSyntax;
            var read = cast?.Expression ?? expression;
            var root = Root(read);
            var side = root is null ? null : _call.Model.GetSymbolInfo(root).Symbol;
            var isOuter = SymbolEqualityComparer.Default.Equals(side, parameters[0]);
            if (root is null || (!isOuter && !SymbolEqualityComparer.Default.Equals(side, parameters[1])))
            {
                Fail(expression, $"a member of a {step.Name}'s result is a field of one side, or the side itself: {OuterJoinShape}");
                return null;
            }

            var name = initializer.NameEquals?.Name.Identifier.ValueText ?? (read as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? root.Identifier.ValueText;
            var text = read.WithoutTrivia().ToString();
            if ((isOuter ? outerOptional : innerOptional) && read is MemberAccessExpressionSyntax)
            {
                var type = _call.Model.GetTypeInfo(read).Type;
                var castType = cast is null ? null : _call.Model.GetTypeInfo(cast).Type;
                if (type is { IsValueType: true } && !IsNullableValue(type) && !IsNullableValue(castType))
                {
                    Fail(expression, $"a {step.Name} reads the fields of a side that may be missing as null: cast the member to its nullable type (({type.ToDisplayString()}?){read.WithoutTrivia()})");
                    return null;
                }

                // p.A.B as p?.A.B: the access short-circuits the whole chain when the side is missing.
                var identifier = root.Identifier.Text;
                text = identifier + "?" + text.Substring(identifier.Length);
            }

            if (cast is not null && _call.Model.GetTypeInfo(cast).Type is { } target)
                text = $"({target.ToDisplayString(NullableQualified)}){text}";
            members.Add($"{name} = {text}");
        }

        var outerType = _entity.FullName + (outerOptional ? "?" : "");
        var innerTypeName = innerType.ToDisplayString(DocModel.Qualified) + (innerOptional ? "?" : "");
        var outerRead = outerOptional ? $"({outerType})__outer" : $"({outerType})__outer!";
        var innerRead = innerOptional ? $"({innerTypeName})__inner" : $"({innerTypeName})__inner!";
        return $"static (__outer, __inner) => {{ var {parameters[0].Name} = {outerRead}; var {parameters[1].Name} = {innerRead}; return new {{ {string.Join(", ", members)} }}; }}";
    }

    /// <summary>The parameter a member chain starts at (<c>c</c> in <c>c.Address.City</c>, or <c>c</c> itself); null for anything else.</summary>
    private static IdentifierNameSyntax? Root(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier;
                case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax } access:
                    expression = access.Expression;
                    continue;
                default:
                    return null;
            }
        }
    }

    private static bool IsNullableValue(ITypeSymbol? type) => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
}
