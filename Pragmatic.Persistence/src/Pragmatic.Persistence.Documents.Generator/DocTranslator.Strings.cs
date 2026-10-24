using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Documents.Generator;

/// <summary>
///     A string field compared without its case, as Pragmatic's <c>[Filter(IgnoreCase = true)]</c> writes it: <c>e.Text.ToLower()</c> (or
///     <c>ToLowerInvariant()</c>) with <c>==</c>, <c>!=</c>, Contains, StartsWith or EndsWith, the value lowered by the caller. MongoDB matches it
///     with a <c>$regex</c> and <c>$options: "i"</c>, Cosmos DB with the functions' ignore-case argument (<c>STRINGEQUALS(…, true)</c>). Any other
///     use of a field's ToLower is refused.
/// </summary>
internal sealed partial class DocTranslator
{
    /// <summary>The path of a string field read through its <c>ToLower()</c>; null for any other expression.</summary>
    private string? LoweredPath(ExpressionSyntax expression, out DocField? field)
    {
        field = null;
        if (expression is ParenthesizedExpressionSyntax paren)
            return LoweredPath(paren.Expression, out field);
        if (expression is not InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0, Expression: MemberAccessExpressionSyntax access }
            || _model.GetSymbolInfo(expression).Symbol is not IMethodSymbol { Name: "ToLower" or "ToLowerInvariant", ContainingType.SpecialType: SpecialType.System_String })
            return null;
        return Path(access.Expression, out field) is { } path && field?.Kind == DocKind.String ? path : null;
    }

    /// <summary>An equality or an inequality of a lowered field with a value; false when neither side is one.</summary>
    private bool LoweredComparison(BinaryExpressionSyntax comparison, string op, out DocCondition? condition)
    {
        condition = null;
        var lowered = LoweredPath(comparison.Left, out var field) is { } left ? (Path: left, Value: comparison.Right)
            : LoweredPath(comparison.Right, out field) is { } right ? (Path: right, Value: comparison.Left) : default;
        if (lowered.Path is null)
            return false;
        if (op is not ("$eq" or "$ne"))
        {
            condition = Fail(comparison, "a field's ToLower() is compared with == or !=, or matched with Contains, StartsWith, EndsWith");
            return true;
        }

        if (Value(lowered.Value, field) is not { } value)
            return true;
        var match = new DocCondition.Match(lowered.Path, value, start: true, end: true, ignoreCase: true);
        condition = op == "$eq" ? match : new DocLogical("$nor", [match]);
        return true;
    }
}
