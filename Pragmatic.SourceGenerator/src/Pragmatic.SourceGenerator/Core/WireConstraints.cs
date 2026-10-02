namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     What a property's validation attributes say about the values it accepts, in the vocabulary
///     of JSON Schema — the part of the server's rules the published contract can carry.
/// </summary>
/// <remarks>
///     <para>
///         Every member is a JSON Schema keyword, so the same record describes a request body
///         property in the manifest and the schema property in the OpenAPI document, and nothing has
///         to be translated on the way. Numbers are <see cref="double" /> because <c>[Range]</c>
///         takes an <c>int</c>, a <c>double</c> or a decimal string and the document has one number
///         type; a value the attribute holds as an <c>int</c> renders without a fraction.
///     </para>
///     <para>
///         Length bounds are split by what they count. <c>[MaxLength]</c> on a string is
///         <c>maxLength</c> and on a collection is <c>maxItems</c>: the attribute is one, the keyword
///         is not, and <c>maxLength</c> on an array is a keyword no reader applies.
///     </para>
/// </remarks>
internal sealed record WireConstraints
{
    public static readonly WireConstraints Empty = new();

    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? Pattern { get; init; }

    /// <summary>A JSON Schema format the property's type does not already imply: email, uri, uuid.</summary>
    public string? Format { get; init; }

    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? ExclusiveMinimum { get; init; }
    public double? ExclusiveMaximum { get; init; }
    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }

    public bool IsEmpty
        => MinLength is null && MaxLength is null && Pattern is null && Format is null
           && Minimum is null && Maximum is null && ExclusiveMinimum is null && ExclusiveMaximum is null
           && MinItems is null && MaxItems is null;
}
