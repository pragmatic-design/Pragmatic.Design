namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a body property for DTO generation.
/// </summary>
internal sealed record BodyPropertyModel
{
    /// <summary>
    ///     The property name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The fully qualified type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the property is required.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     The initializer written on the source property, copied onto the generated one.
    /// </summary>
    /// <remarks>
    ///     Without it a non-required, non-nullable property with a default arrives as <c>null</c> when
    ///     the caller omits the field: the type says it cannot be null, the declaration says what it
    ///     defaults to, and the action gets neither.
    /// </remarks>
    public string? DefaultValueSyntax { get; init; }

    /// <summary>
    ///     XML documentation summary.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    ///     Whether this property implicitly binds to body (no explicit binding attribute).
    /// </summary>
    public bool IsImplicit { get; init; }

    /// <summary>
    ///     Whether the property type is a scalar (primitive, string, Guid, DateTime, enum).
    ///     Scalar types cannot be passed directly as [FromBody] and need a DTO wrapper.
    /// </summary>
    public bool IsScalar { get; init; }

    /// <summary>
    ///     The name this property carries on the wire, when the author renamed it with
    ///     <c>[JsonPropertyName]</c>.
    /// </summary>
    /// <remarks>
    ///     The body is a record the generator writes, so an attribute on the operation's property does
    ///     not reach it by itself: without carrying it, the wire name is the property name and there is
    ///     no way to say otherwise. Query-string parameters have the equivalent through
    ///     <c>[FromQuery(Name = …)]</c>; this is the body's half of the same ability, and it is the
    ///     standard attribute rather than one of ours.
    /// </remarks>
    public string? JsonName { get; init; }

    /// <summary>
    ///     What the property's validation attributes say about the values it accepts, for the
    ///     published contract.
    /// </summary>
    /// <remarks>
    ///     Carried for the published contract, which had a hole shaped exactly like this one: the
    ///     OpenAPI generator reads <c>maxLength</c> for request-body properties and the manifest never
    ///     wrote it, because nothing upstream had it. The constraint was covered by a test feeding a
    ///     hand-written manifest, so the gap sat behind a green assertion — the field was read, and on
    ///     the real path never arrived. And once it did, it arrived alone: every other rule the server
    ///     applied stayed out of the document.
    /// </remarks>
    public Core.WireConstraints Constraints { get; init; } = Core.WireConstraints.Empty;

    /// <summary>Whether the property's type is an enum, which decides its shape on the wire.</summary>
    public bool IsEnum { get; init; }

    /// <summary>
    ///     The <c>TemporalJsonBehavior</c> the source property declared, when it carries one of the six
    ///     timezone attributes on a supported type; null otherwise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Carried here because the conversion is keyed on the type that is <b>deserialized</b>,
    ///         and that is this record — not the operation the author wrote the attribute on. Registered
    ///         against the operation, which nothing deserializes, the generated record would carry the
    ///         property's <em>documentation describing the conversion</em> while disabling it.
    ///     </para>
    ///     <para>
    ///         The same sentence as <see cref="JsonName" />, one mechanism further: an attribute on the
    ///         author's property does not reach a record the generator writes. That one is carried by
    ///         emitting the attribute; this one cannot be — a generator does not see its own output, so
    ///         re-emitting <c>[FromClientTimezone]</c> here would be inert a second time. What travels
    ///         is the <em>registration</em>, which the Temporal feature emits for this type.
    ///     </para>
    /// </remarks>
    public string? TemporalBehavior { get; init; }

    /// <summary>
    ///     The version when this property was introduced (from [SinceVersion]).
    ///     Null means the property is available since version 1.0.
    /// </summary>
    public string? SinceVersion { get; init; }
}
