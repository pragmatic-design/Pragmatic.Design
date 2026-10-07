namespace Pragmatic.SourceGenerator.Features.Logging.Models;

/// <summary>A parameter of a call site, with everything the generated state needs to write it.</summary>
internal sealed record LogParameterModel
{
    /// <summary>Its name, as the generated signature repeats it.</summary>
    public required string Name { get; init; }

    /// <summary>Its type, fully qualified and with its nullable annotation, as the signature repeats it.</summary>
    public required string Type { get; init; }

    /// <summary>What it is for.</summary>
    public required LogParameterRole Role { get; init; }

    /// <summary>Whether it is also a structured property: always for a property, for an exception when the template names it.</summary>
    public required bool IsProperty { get; init; }

    /// <summary>The structured property's key: the placeholder as the template writes it, else the name.</summary>
    public required string Key { get; init; }

    /// <summary>How its value is written.</summary>
    public required LogValueKind Kind { get; init; }

    /// <summary>Whether it is a <c>Nullable&lt;T&gt;</c>.</summary>
    public required bool IsNullableValueType { get; init; }

    /// <summary>
    ///     Whether it is written as the mask: its parameter is marked <c>[NotLogged]</c> or
    ///     <c>[PersonalData]</c>. Decided here, so nothing is decided per call.
    /// </summary>
    public required bool IsMasked { get; init; }

    /// <summary>
    ///     The format a <see cref="LogValueKind.Formattable" /> value is written with as a structured
    ///     property: the Pragmatic JSON provider's (<c>O</c> for dates, <c>c</c> for a time span,
    ///     <c>D</c> for a GUID), or empty.
    /// </summary>
    public required string JsonFormat { get; init; }

    /// <summary>The <c>Utf8JsonWriter.WriteNumber</c> argument type a <see cref="LogValueKind.Number" /> is widened to, or empty.</summary>
    public required string NumberType { get; init; }

    /// <summary>The delegate that writes a <see cref="LogValueKind.Json" /> value, or empty.</summary>
    public string JsonWriter { get; init; } = "";

    /// <summary>Every writer method a <see cref="LogValueKind.Json" /> value needs, for the assembly's writer file.</summary>
    public SourceGen.EquatableArray<Serialization.Models.JsonWriterMethodModel> JsonWriterMethods { get; init; }
        = SourceGen.EquatableArray<Serialization.Models.JsonWriterMethodModel>.Empty;
}
