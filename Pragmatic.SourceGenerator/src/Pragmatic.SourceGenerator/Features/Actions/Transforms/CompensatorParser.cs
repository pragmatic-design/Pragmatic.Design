using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Reads <c>[UndoWith&lt;T&gt;]</c> and checks that <c>T</c> compensates what the action returns.
/// </summary>
/// <remarks>
///     The check has to happen here rather than in the type system: the attribute's own type parameter
///     cannot be constrained to <c>ICompensates&lt;TReturn&gt;</c>, because the attribute does not know
///     the action's return type — the action does. So the constraint is a diagnostic (PRAG0425), and a
///     mismatch would otherwise surface as a cast that never runs.
/// </remarks>
internal static class CompensatorParser
{
    private const string AttributeName = "UndoWithAttribute";
    private const string AttributeNamespace = "Pragmatic.Actions.Attributes";
    private const string CompensatesInterface = "ICompensates";
    private const string CompensatesVoidInterface = "ICompensatesVoid";
    private const string CompensationNamespace = "Pragmatic.Actions.Compensation";

    /// <summary>
    ///     Returns the declared compensator, and — when it does not compensate this action's return
    ///     type — its name, for the diagnostic.
    /// </summary>
    /// <param name="symbol">The action type.</param>
    /// <param name="returnType">What the action returns, or <c>null</c> for a void action.</param>
    public static (string? CompensatorTypeName, string? Mismatched) Parse(
        INamedTypeSymbol symbol, ITypeSymbol? returnType)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition;
            if (originalDef.Name != AttributeName
                || originalDef.ContainingNamespace?.ToDisplayString() != AttributeNamespace)
                continue;

            if (attrClass.TypeArguments.Length == 0
                || attrClass.TypeArguments[0] is not INamedTypeSymbol compensator)
                continue;

            var display = compensator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return Compensates(compensator, returnType)
                ? (display, null)
                : (null, compensator.Name);
        }

        return (null, null);
    }

    private static bool Compensates(INamedTypeSymbol compensator, ITypeSymbol? returnType)
    {
        foreach (var iface in compensator.AllInterfaces)
        {
            var ns = iface.OriginalDefinition.ContainingNamespace?.ToDisplayString();
            if (ns != CompensationNamespace)
                continue;

            if (returnType is null)
            {
                if (iface.OriginalDefinition.Name == CompensatesVoidInterface)
                    return true;

                continue;
            }

            if (iface.OriginalDefinition.Name != CompensatesInterface || iface.TypeArguments.Length != 1)
                continue;

            // Contravariant by declaration, so a compensator of a base type is a legitimate match —
            // comparing the type arguments for equality would reject it for no reason.
            if (SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], returnType))
                return true;
        }

        return false;
    }
}
