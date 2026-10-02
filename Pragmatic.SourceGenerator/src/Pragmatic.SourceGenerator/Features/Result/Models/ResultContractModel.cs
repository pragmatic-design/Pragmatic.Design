using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Result.Models;

/// <summary>
///     How a declared result type gets its JSON converter.
/// </summary>
internal enum ResultConverterKind
{
    /// <summary>A fixed-arity converter already exists in Pragmatic.Result; just instantiate it.</summary>
    Existing,

    /// <summary>A multi-error variant, whose converter has to be generated for this closed type.</summary>
    GeneratedMultiError,

    /// <summary>A multi-error void variant.</summary>
    GeneratedMultiErrorVoid
}

/// <summary>
///     One error slot of a declared result type.
/// </summary>
/// <param name="TypeName">The error type, fully qualified with global::.</param>
/// <param name="Discriminator">
///     What <c>ErrorTypeRegistry.GetDiscriminator</c> produces for it — <c>Type.FullName</c>, so nested
///     types join with '+'. Computed at compile time so the generated writer and the runtime reader
///     agree on the string; a mismatch resolves to nothing, silently.
/// </param>
internal sealed record ErrorSlotModel(string TypeName, string Discriminator);

/// <summary>
///     One <c>[assembly: JsonResultContract&lt;…&gt;]</c> declaration, resolved.
/// </summary>
/// <param name="ResultTypeName">The closed result type, fully qualified with global::.</param>
/// <param name="ValueTypeName">The success value's type, or null for a void variant.</param>
/// <param name="ValueCanBeNull">
///     Whether the success value can be null. <c>Deserialize&lt;int&gt;</c> returns <c>int</c>, and
///     <c>is null</c> against a non-nullable value type is CS0037 — so the guard is emitted only where
///     it means something.
/// </param>
/// <param name="Kind">Which converter serves this type.</param>
/// <param name="ExistingConverterTypeName">
///     For <see cref="ResultConverterKind.Existing" />, the closed converter type to instantiate.
/// </param>
/// <param name="ConverterName">
///     For a generated converter, its class name — unique within the assembly.
/// </param>
/// <param name="ErrorSlots">
///     The declared error types, in order. The generated converter switches over these instead of
///     resolving the concrete type at run time.
/// </param>
internal sealed record ResultContractModel(
    string ResultTypeName,
    string? ValueTypeName,
    bool ValueCanBeNull,
    ResultConverterKind Kind,
    string? ExistingConverterTypeName,
    string ConverterName,
    EquatableArray<ErrorSlotModel> ErrorSlots)
{
    /// <summary>The expression that produces the converter instance in the registration.</summary>
    public string NewConverterExpression => Kind == ResultConverterKind.Existing
        ? $"new {ExistingConverterTypeName}()"
        : $"new {ConverterName}()";

    /// <summary>Whether a converter has to be written for this contract.</summary>
    public bool IsGenerated => Kind != ResultConverterKind.Existing;
}
