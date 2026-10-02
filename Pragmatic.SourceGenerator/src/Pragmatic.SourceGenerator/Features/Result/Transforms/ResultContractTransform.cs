using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Transforms;

/// <summary>
///     Turns a <c>[assembly: JsonResultContract&lt;TResult&gt;]</c> declaration into the model that says
///     which converter serves the closed type, and whether it has to be generated at all.
/// </summary>
/// <remarks>
///     This dispatch is the compile-time twin of what <c>ResultJsonConverterFactory.CreateConverter</c>
///     did at run time with <c>MakeGenericType</c> and <c>Activator.CreateInstance</c>. Four of the six
///     shapes already have a fixed-arity typed converter in Pragmatic.Result, so for those nothing is
///     generated — the registration just constructs the existing one. Only the multi-error variants,
///     whose arity is open-ended, need a converter written for the closed type.
/// </remarks>
internal static class ResultContractTransform
{
    private const string ResultNamespace = "Pragmatic.Result";

    public static ResultContractModel? From(INamedTypeSymbol? resultType)
    {
        if (resultType is null || resultType.ContainingNamespace?.ToDisplayString() != ResultNamespace)
            return null;

        var arity = resultType.TypeArguments.Length;
        var fullName = Global(resultType);

        switch (resultType.Name)
        {
            case "Result" when arity == 1:
                return Existing(fullName, Global(resultType.TypeArguments[0]),
                    $"global::{ResultNamespace}.Serialization.UntypedResultJsonConverter<{Global(resultType.TypeArguments[0])}>");

            case "Result" when arity == 2:
                return Existing(fullName, Global(resultType.TypeArguments[0]),
                    $"global::{ResultNamespace}.Serialization.ResultJsonConverter<{Global(resultType.TypeArguments[0])}, {Global(resultType.TypeArguments[1])}>");

            case "Result":
                // Result<TValue, TError1, …, TErrorN>. TValue is the first argument, whatever N is.
                return new ResultContractModel(fullName, Global(resultType.TypeArguments[0]),
                    CanBeNull(resultType.TypeArguments[0]),
                    ResultConverterKind.GeneratedMultiError, null, ConverterName(resultType),
                    Slots(resultType, skip: 1));

            case "VoidResult" when arity == 1:
                return Existing(fullName, null,
                    $"global::{ResultNamespace}.Serialization.VoidResultJsonConverter<{Global(resultType.TypeArguments[0])}>");

            case "VoidResult":
                return new ResultContractModel(fullName, null, false,
                    ResultConverterKind.GeneratedMultiErrorVoid, null, ConverterName(resultType),
                    Slots(resultType, skip: 0));

            case "Maybe" when arity == 1:
                return Existing(fullName, Global(resultType.TypeArguments[0]),
                    $"global::{ResultNamespace}.Serialization.MaybeJsonConverter<{Global(resultType.TypeArguments[0])}>");

            default:
                return null;
        }
    }

    private static ResultContractModel Existing(string resultType, string? valueType, string converter)
        => new(resultType, valueType, false, ResultConverterKind.Existing, converter, converter, default);

    /// <summary>True when <c>is null</c> against the deserialized value is legal C#.</summary>
    private static bool CanBeNull(ITypeSymbol type)
        => !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>The declared error slots, skipping the value argument where there is one.</summary>
    private static EquatableArray<ErrorSlotModel> Slots(INamedTypeSymbol resultType, int skip)
    {
        var builder = ImmutableArray.CreateBuilder<ErrorSlotModel>();
        for (var i = skip; i < resultType.TypeArguments.Length; i++)
            builder.Add(new ErrorSlotModel(Global(resultType.TypeArguments[i]),
                MetadataFullName(resultType.TypeArguments[i])));

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Reproduces <c>Type.FullName</c>, which is what ErrorTypeRegistry uses as the discriminator.
    /// </summary>
    /// <remarks>
    ///     Not <c>ToDisplayString()</c>: a nested type is <c>Ns.Outer+Inner</c> to the runtime and
    ///     <c>Ns.Outer.Inner</c> to Roslyn, and a discriminator that does not match byte for byte fails
    ///     to resolve — silently, into SerializedError.
    /// </remarks>
    private static string MetadataFullName(ITypeSymbol type)
    {
        var names = new List<string> { type.Name };
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
            names.Insert(0, containing.Name);

        var nested = string.Join("+", names);
        var ns = type.ContainingNamespace;

        return ns is null || ns.IsGlobalNamespace ? nested : $"{ns.ToDisplayString()}.{nested}";
    }

    /// <summary>
    ///     A converter name built from every type argument, so two contracts over the same generic
    ///     definition never collide on the hint name — which Roslyn answers by discarding the whole
    ///     generator's output with a CS8785 that is only a warning.
    /// </summary>
    private static string ConverterName(INamedTypeSymbol resultType)
    {
        var parts = resultType.TypeArguments.Select(t => t.Name);
        return $"{resultType.Name}{string.Concat(parts)}JsonConverter";
    }

    private static string Global(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
