using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents property metadata read from assembly attributes.
/// </summary>
internal sealed record PropertyMetadataModel
{
    /// <summary>
    ///     The property name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The property type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the property has a private setter.
    /// </summary>
    public bool HasPrivateSetter { get; init; }

    /// <summary>
    ///     Whether the property is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Maximum length for string properties.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    ///     Precision for decimal properties.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    ///     Scale for decimal properties.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    ///     Whether this property is the logic key.
    /// </summary>
    public bool IsLogicKey { get; init; }

    /// <summary>
    ///     Whether the property has a default value initializer.
    /// </summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>
    ///     Whether the property type is an enum (EF Core handles enums natively).
    /// </summary>
    public bool IsEnum { get; init; }

    /// <summary>
    ///     Whether this is a navigation property (FK or collection).
    /// </summary>
    public bool IsNavigation { get; init; }

    /// <summary>
    ///     Whether this property is required for entity creation.
    ///     A property is required if: not nullable, no default value, not PersistenceId,
    ///     not audit/softdelete properties.
    /// </summary>
    public bool IsRequiredForCreate { get; init; }

    /// <summary>
    ///     Whether the member is declared with the C# <c>required</c> modifier.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the same question as <see cref="IsRequiredForCreate" />, which is about what the
    ///     factory asks the caller for. This one is about what the <b>compiler</b> refuses: an object
    ///     initializer that omits a <c>required</c> member is CS9035, so every generated <c>new</c>
    ///     has to name it, even where the value it can supply is nothing at all.
    /// </remarks>
    public bool IsRequiredMember { get; init; }

    /// <summary>
    ///     The static default value expression from [DefaultValue(...)], if any.
    ///     Example: "\"EUR\"" for strings, "1" for ints.
    /// </summary>
    public string? DefaultValueExpression { get; init; }

    /// <summary>
    ///     The fully-qualified name of the computed default generator type
    ///     from [ComputedDefault&lt;TEntity, TValue, TGenerator&gt;], if any.
    /// </summary>
    public string? ComputedDefaultGeneratorFqn { get; init; }

    /// <summary>
    ///     What this property is called on the wire, from <c>[JsonPropertyName]</c>, when that differs
    ///     from its own name.
    /// </summary>
    /// <remarks>
    ///     Not about the column — it never reaches the database. An entity returned by an endpoint is
    ///     published as a schema, and that schema described the property under the name the class gave
    ///     it while the serializer wrote the renamed one. A response under a name the client does not
    ///     expect is a null nobody reports, which is quieter than the request half of the same bug.
    /// </remarks>
    public string? WireName { get; init; }

    /// <summary>
    ///     Previous property/column name from [RenamedFrom("...")].
    ///     Used by the migration diff engine to generate RENAME COLUMN instead of DROP + ADD.
    /// </summary>
    public string? RenamedFrom { get; init; }

    /// <summary>
    ///     When this property's type is a <c>[ValueObject]</c>, the flattened complex-type columns
    ///     (<c>{Property}_{SubProperty}</c>). Empty for ordinary scalar properties. Drives both the EF
    ///     <c>ComplexProperty</c> mapping and the migration schema columns (kept in sync).
    /// </summary>
    public EquatableArray<ValueObjectColumnModel> ValueObjectColumns { get; init; } = EquatableArray<ValueObjectColumnModel>.Empty;

    /// <summary>Whether this property's type is a <c>[ValueObject]</c> mapped as an EF complex type.</summary>
    public bool IsValueObject => !ValueObjectColumns.IsDefaultOrEmpty;
}
