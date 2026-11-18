using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Serialization;

/// <summary>
///     Enums as their names: the converter the generated host registers for every enum, in place of
///     <see cref="JsonStringEnumConverter" />, which writes the same bytes.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why not <see cref="JsonStringEnumConverter" /> itself.</b> Its constructor requires dynamic code, so a host that
///         creates one cannot be published Native AOT without a warning (#857), whether or not anything ever asks it for a
///         converter. This one requires nothing to create.
///     </para>
///     <para>
///         <b>Who builds the converter.</b> The JSON contexts the generator writes recognise this type and build
///         <c>JsonStringEnumConverter&lt;TEnum&gt;</c> for their own enums, where <c>TEnum</c> is known, so nothing is made at
///         run time. An enum that reaches <see cref="CreateConverter" /> instead is one no generated context covers: with dynamic
///         code available, which is the JIT, it is handed to <see cref="JsonStringEnumConverter" />, so the reflection path
///         writes what it always wrote. Without it, under Native AOT, nothing can make that converter, and the refusal names the
///         enum rather than failing inside the serializer.
///     </para>
///     <para>
///         It claims every enum, as the framework converter does, so what claims which type in a host's options is what it was.
///     </para>
/// </remarks>
public sealed class PragmaticStringEnumConverter : JsonConverterFactory
{
    private JsonConverterFactory? _reflective;

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            _reflective ??= new JsonStringEnumConverter();
            return _reflective.CreateConverter(typeToConvert, options);
        }

        throw new NotSupportedException(
            $"The enum {typeToConvert} is not covered by a generated JSON context, and without dynamic code nothing can write it as " +
            "its name. Put the type that carries it in a response, request or message the generator sees, or register " +
            $"JsonStringEnumConverter<{typeToConvert.Name}> for it.");
    }
}
