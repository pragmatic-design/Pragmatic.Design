using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Decides which <c>RequestBinder</c> overload a parameter's type needs.
/// </summary>
internal static class BindKindClassifier
{
    /// <summary>Classifies a parameter type, seeing through <c>Nullable&lt;T&gt;</c>.</summary>
    public static BindKind Classify(ITypeSymbol type)
    {
        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                         && nullable.TypeArguments.Length == 1
            ? nullable.TypeArguments[0]
            : type;

        if (underlying.SpecialType == SpecialType.System_String)
            return BindKind.String;

        if (underlying.TypeKind == TypeKind.Enum)
            return BindKind.Enum;

        // IParsable<TSelf> is what the BCL puts on everything bindable from text — the numeric types,
        // DateTime and friends, Guid, bool. Asking the type rather than listing them keeps a
        // user-defined parsable type working for free.
        foreach (var iface in underlying.AllInterfaces)
            if (iface.OriginalDefinition.ToDisplayString() == "System.IParsable<TSelf>")
                return BindKind.Parsable;

        return BindKind.Complex;
    }
}
