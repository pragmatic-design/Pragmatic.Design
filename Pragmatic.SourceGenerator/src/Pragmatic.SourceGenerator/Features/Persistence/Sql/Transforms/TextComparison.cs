using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The <c>StringComparison</c> of a <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c> on a text field, as both engines translate it: ordinal
///     is the match without one, ignoring case is the match without case (Sql: <c>LOWER</c> on both sides; documents: the regex's <c>i</c>, Cosmos DB's
///     third argument). A culture's rules have no translation the engines can keep to, and are refused.
/// </summary>
internal static class TextComparison
{
    // System.StringComparison by number: the argument is a constant of the enum.
    private const int CurrentCulture = 0, CurrentCultureIgnoreCase = 1, InvariantCulture = 2, InvariantCultureIgnoreCase = 3, Ordinal = 4, OrdinalIgnoreCase = 5;

    /// <summary>
    ///     True to ignore case, false for the ordinal match; null when the comparison is not one the engines translate, with
    ///     <paramref name="refused" /> saying why.
    /// </summary>
    public static bool? IgnoresCase(SemanticModel model, ExpressionSyntax comparison, out string refused)
    {
        refused = "";
        if (model.GetTypeInfo(comparison).Type?.ToDisplayString() != "System.StringComparison" || model.GetConstantValue(comparison).Value is not int value)
        {
            refused = "the StringComparison must be a constant (StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase)";
            return null;
        }

        switch (value)
        {
            case Ordinal:
                return false;
            case OrdinalIgnoreCase or InvariantCultureIgnoreCase:
                return true;
            case CurrentCulture or CurrentCultureIgnoreCase or InvariantCulture:
                refused = $"StringComparison.{Name(value)} compares by a culture's rules, which the database does not apply; use Ordinal or OrdinalIgnoreCase";
                return null;
            default:
                refused = $"StringComparison {value} is not translated";
                return null;
        }
    }

    private static string Name(int value) => value switch
    {
        CurrentCulture => "CurrentCulture",
        CurrentCultureIgnoreCase => "CurrentCultureIgnoreCase",
        _ => "InvariantCulture",
    };
}
