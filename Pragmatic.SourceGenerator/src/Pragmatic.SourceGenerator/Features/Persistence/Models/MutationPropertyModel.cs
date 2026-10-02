using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a property in a mutation DTO.
///     Simplified version of PropertyMappingModel focused on mutation needs.
/// </summary>
internal sealed record MutationPropertyModel
{
    /// <summary>
    ///     The name of the DTO property.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The full type name of the property.
    /// </summary>
    public required string PropertyType { get; init; }

    /// <summary>
    ///     The property's type in the <c>global::</c> form, for generated code that names it.
    /// </summary>
    public string PropertyFullTypeName { get; init; } = "";

    /// <summary>
    ///     Whether the property type is nullable.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether the property can be written from outside — a <c>set</c> or <c>init</c> accessor.
    ///     A body cannot fill what has neither, so the JSON converter leaves it out.
    /// </summary>
    public bool HasSetter { get; init; }

    /// <summary>
    ///     Whether the property is <c>required</c>: an object initializer has to name it, and there
    ///     is no initializer on it to read a default from.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the underlying property type is a value type (struct, enum, primitive).
    ///     Used to determine if .Value is needed when unwrapping nullable.
    /// </summary>
    public bool IsValueType { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Mapping Configuration (from Pragmatic.Mapping attributes)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this property is explicitly ignored ([MapIgnore]).
    /// </summary>
    public bool IsIgnored { get; init; }

    /// <summary>
    ///     The target property name on the entity (from [MapProperty] or same as PropertyName).
    /// </summary>
    public string TargetPropertyName { get; init; } = "";

    /// <summary>
    ///     The converter type full name (from [MapConverter]).
    /// </summary>
    public string? ConverterType { get; init; }

    /// <summary>
    ///     Whether this property uses a converter.
    /// </summary>
    public bool HasConverter => !string.IsNullOrEmpty(ConverterType);

    // ═══════════════════════════════════════════════════════════════════════════
    // Nested & Collection Mapping
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether this is a nested DTO that requires recursive ApplyTo.
    /// </summary>
    public bool IsNestedMutation { get; init; }

    /// <summary>
    ///     The nested mutation DTO type name (if IsNestedMutation).
    /// </summary>
    public string? NestedMutationType { get; init; }

    /// <summary>
    ///     Whether this is a collection property.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The collection kind (List, Array, ICollection, etc.).
    /// </summary>
    public CollectionKind CollectionKind { get; init; }

    /// <summary>
    ///     Whether the collection elements are mutation DTOs.
    /// </summary>
    public bool IsElementMutation { get; init; }

    /// <summary>
    ///     The element mutation DTO type for collection mapping.
    /// </summary>
    public string? ElementMutationType { get; init; }

    /// <summary>
    ///     The entity type for collection elements (to create new instances).
    /// </summary>
    public string? ElementEntityType { get; init; }

    /// <summary>
    ///     How this collection is written back and what its elements are matched by.
    ///     Null when the property is not a collection of DTOs.
    /// </summary>
    public CollectionWriteModel? CollectionWrite { get; init; }

    /// <summary>
    ///     Whether the related DTO — the collection element, or the nested DTO itself — can build a new
    ///     entity. <c>[MapTo&lt;T&gt;]</c> is what gives it a <c>ToEntity()</c>.
    /// </summary>
    public bool RelatedCanCreate { get; init; }

    /// <summary>
    ///     Whether the related DTO can update an existing entity through <c>ApplyTo()</c>.
    /// </summary>
    public bool RelatedCanUpdate { get; init; }

    /// <summary>
    ///     Whether the related DTO can update an existing entity through <c>ApplyPatch()</c> — it is
    ///     itself a <c>[Patch&lt;T&gt;]</c>.
    /// </summary>
    public bool RelatedCanPatch { get; init; }

    /// <summary>
    ///     The related DTO's type name — the collection's element, or the nested DTO. Used to name it
    ///     in diagnostics.
    /// </summary>
    public string? RelatedDtoType { get; init; }

    /// <summary>The related DTO's fully qualified name, or null when there is no related DTO.</summary>
    /// <remarks>
    ///     Generated code names this type, so it needs the <c>global::</c> form.
    ///     <see cref="RelatedDtoType" /> is the readable one, used in diagnostic messages.
    /// </remarks>
    public string? RelatedDtoFullTypeName { get; init; }

    /// <summary>
    ///     How a child that is one, not many, is written — from <c>[ReferenceStrategy]</c> on this
    ///     property. <c>Merge</c> unless the author said otherwise.
    /// </summary>
    public string ReferenceStrategy { get; init; } = "Merge";

    /// <summary>
    ///     Whether the related type is declared in this compilation.
    /// </summary>
    /// <remarks>
    ///     What separates "a DTO of yours that cannot write its entity" — worth reporting — from
    ///     <c>List&lt;string&gt;</c>, which was never a child in the first place.
    /// </remarks>
    public bool RelatedIsUserType { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Entity Property Info
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Whether the target entity property has a private setter.
    /// </summary>
    public bool EntityHasPrivateSetter { get; init; }

    /// <summary>
    ///     Whether the target entity property exists.
    /// </summary>
    public bool EntityPropertyExists { get; init; }

    /// <summary>
    ///     The type of the target entity property.
    /// </summary>
    public string? EntityPropertyType { get; init; }
}

/// <summary>
///     Enum for collection kinds (shared with Mapping).
/// </summary>
internal enum CollectionKind
{
    None,
    List,
    Array,
    HashSet,
    IList,
    ICollection,
    IEnumerable,
    IReadOnlyList,
    IReadOnlyCollection
}
