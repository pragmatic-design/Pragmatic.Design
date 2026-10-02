using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Validation.Analysis;

/// <summary>
///     Whether a type will implement <c>ISyncValidator</c> once generation has run.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Asking <c>type.AllInterfaces.Contains(ISyncValidator)</c> answers <b>no</b> for every
///         type in the compilation being generated: the interface is added by the validation
///         generator, and one generator cannot see another's output. It answers yes only for a type
///         from an already-compiled assembly — which is the rare case, not the normal one.
///     </para>
///     <para>
///         So nested validation would select almost nothing. This takes the same shape Persistence
///         uses with <c>TraitPropertyResolver</c>: do not look for the member, ask the same question
///         the generator that emits it asks.
///     </para>
///     <para>
///         ⚠️ And ask it <b>the same way</b>. "Does this type carry validation attributes?" is an
///         approximation of the generator's criterion, not the criterion: the generator also emits a
///         <c>Validate()</c> for a C# <c>required</c> member, for a collection of validatables, for a
///         nested validatable. With the approximation, a child mutation that validates for
///         <c>required</c> alone would have a <c>Validate()</c> that <c>ValidateNestedTree()</c> never
///         calls. Two predicates for one question drift; there is one, and it is the generator's.
///     </para>
/// </remarks>
internal static class ValidatablePredictor
{
    private const string SyncValidator = "Pragmatic.Validation.ISyncValidator";

    /// <summary>Whether <paramref name="type"/> validates itself, now or after generation.</summary>
    public static bool WillHaveSyncValidator(ITypeSymbol? type, Compilation compilation, CancellationToken ct = default)
    {
        if (type is not INamedTypeSymbol named)
            return false;

        // Already there: a type from an assembly that has been through generation, or one that
        // wrote its own.
        var syncValidator = compilation.GetTypeByMetadataName(SyncValidator);
        if (syncValidator is not null
            && named.AllInterfaces.Contains(syncValidator, SymbolEqualityComparer.Default))
            return true;

        // A base type's Validate() is inherited: any type in the chain the generator will emit for
        // gives the derived one a Validate() to call.
        for (var current = named; current is not null; current = current.BaseType)
            if (ValidationFeature.WillGenerateValidatorFor(current, compilation, ct))
                return true;

        return false;
    }

    /// <summary>
    ///     The element type of a collection, or null when the type is not one.
    /// </summary>
    /// <remarks>
    ///     A string is an <c>IEnumerable&lt;char&gt;</c> and is not a collection of validatables.
    /// </remarks>
    public static ITypeSymbol? ElementOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        if (type.SpecialType == SpecialType.System_String)
            return null;

        foreach (var candidate in type.AllInterfaces)
            if (candidate is { IsGenericType: true, Name: "IEnumerable", TypeArguments.Length: 1 })
                return candidate.TypeArguments[0];

        return null;
    }
}
