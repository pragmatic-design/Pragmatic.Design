using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating a canonical GridFilterRequest → IQueryable bridge for an entity.
///     The bridge converts runtime string-based field names into compile-time typed expressions.
/// </summary>
internal sealed record GridFilterBridgeModel
{
    /// <summary>
    ///     The entity namespace.
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     The entity type name.
    /// </summary>
    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     The fully qualified entity type name.
    /// </summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>
    ///     The filterable/sortable properties on the entity.
    /// </summary>
    public EquatableArray<GridFilterBridgePropertyModel> Properties { get; init; } =
        EquatableArray<GridFilterBridgePropertyModel>.Empty;

    /// <summary>
    ///     Properties the entity has and the bridge deliberately will not filter on.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A denylist that works purely by omission makes the security control indistinguishable
    ///     from a typo: a clause naming <c>TenantId</c> and a clause naming <c>Naem</c> would both fall
    ///     through the switch and both leave the query unchanged. Carrying the
    ///     withheld names here lets the generated switch answer them separately, so a client — and a
    ///     log — can tell "you may not filter on that" from "you spelled it wrong".
    /// </remarks>
    public EquatableArray<string> WithheldProperties { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether this model is valid for code generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(EntityTypeName) &&
                           !string.IsNullOrEmpty(EntityFullTypeName) &&
                           Properties.Length > 0;
}

/// <summary>
///     A single entity property exposed through the canonical bridge.
/// </summary>
internal sealed record GridFilterBridgePropertyModel
{
    /// <summary>
    ///     The property name on the entity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The CLR type name (e.g., "string", "int", "System.Guid").
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the property is a string type.
    /// </summary>
    public bool IsString { get; init; }

    /// <summary>
    ///     Whether the property type is comparable (supports >, >=, &lt;, &lt;=).
    /// </summary>
    public bool IsComparable { get; init; }

    /// <summary>
    ///     Whether the property is an enum type.
    /// </summary>
    public bool IsEnum { get; init; }

    /// <summary>
    ///     Whether the property is a boolean type.
    /// </summary>
    public bool IsBool { get; init; }
}
