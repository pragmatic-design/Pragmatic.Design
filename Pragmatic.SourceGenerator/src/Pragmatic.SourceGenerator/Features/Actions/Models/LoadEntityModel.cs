using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

internal sealed record LoadEntityModel
{
    public required string EntityTypeFullName { get; init; }
    public required string EntityTypeShortName { get; init; }
    public required string IdPropertyName { get; init; }
    public required string IdTypeFullName { get; init; }

    /// <summary>
    ///     Whether the key is a <c>Nullable&lt;TKey&gt;</c>: the entity is loaded only when the key has a
    ///     value, and the field is nullable.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    ///     Whether this is a <c>[LoadEntities]</c>: the key property is a list of keys, the rows are read in one
    ///     query, and the field is an <c>IReadOnlyList</c> of them in the order of the keys.
    /// </summary>
    public bool IsMany { get; init; }

    /// <summary>
    ///     Whether this is a <c>[RequireExists]</c>: the row is checked with <c>ExistsAsync</c> and not read — no field,
    ///     nothing handed to the operation, only its repository.
    /// </summary>
    public bool ExistsOnly { get; init; }

    /// <summary>The entity's key type, fully qualified — the element of the key list of a <see cref="IsMany" />.</summary>
    public string KeyTypeFullName { get; init; } = "global::System.Guid";

    /// <summary>
    ///     The rule the rows are read by instead of a key — the specification member, fully qualified — or
    ///     null for the key form.
    /// </summary>
    public string? SpecificationMember { get; init; }

    /// <summary>Whether <see cref="SpecificationMember" /> is a method, called with <see cref="SpecificationArguments" />.</summary>
    public bool SpecificationIsInvocation { get; init; }

    /// <summary>The rule's parameters and the operation's properties bound to them, by name.</summary>
    public EquatableArray<SpecificationArgumentModel> SpecificationArguments { get; init; } =
        EquatableArray<SpecificationArgumentModel>.Empty;

    /// <summary>Whether the rows are read by a rule rather than by a key.</summary>
    public bool IsBySpecification => SpecificationMember is not null;

    /// <summary>
    ///     The entity's <c>[LogicKey]</c> member the key property holds — <c>[LoadEntity(By = …)]</c> — or null when
    ///     the key is the id.
    /// </summary>
    public string? LogicKeyMember { get; init; }

    /// <summary>
    ///     The generated class holding the logic-key lookup, fully qualified — <c>global::Ns.EmployeeSpecifications</c>,
    ///     with <c>GetBy{Key}Async</c> and <c>By{Key}</c>.
    /// </summary>
    public string? LogicKeyLookupClass { get; init; }

    /// <summary>Whether no row is a 404 rather than an empty list — <c>[LoadEntities(RequireAny = true)]</c>.</summary>
    public bool RequireAny { get; init; }

    /// <summary>Whether the caller must hold the entity's read permission before the rows are read.</summary>
    public bool RequireReadPermission { get; init; }

    /// <summary>
    ///     The entity's read permission, found in the permission catalogue by the entity; null until resolved,
    ///     and after it when the catalogue has none (<c>PRAG0457</c>).
    /// </summary>
    public string? ReadPermission { get; init; }

    /// <summary>The navigation paths loaded with the row (<c>Include</c>), each checked against the entity.</summary>
    public EquatableArray<string> Includes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The loaded entity's <c>[Invariant]</c> rules this operation can answer: the ones whose body
    ///     reads nothing outside <see cref="Includes" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Filtered here rather than in the template, so the model answers the question once and the
    ///         two halves cannot read it differently. The mutation invoker is not the only place an
    ///         invariant is checked: an aggregate moved by a <c>[DomainAction]</c> — which is how a
    ///         state machine is moved — has its rules evaluated by the action invoker too.
    ///     </para>
    ///     <para>
    ///         ⚠️ A rule reading a navigation this operation did not include is left <b>out</b>, and that
    ///         is the whole design: voiding an invoice includes its lines and not its payments, so a rule
    ///         over the payments would compare a real amount against an empty collection and refuse a
    ///         request that must succeed. Skipping fails closed, while checking what cannot be seen is a new wrong answer, and a 422 at that.
    ///     </para>
    ///     <para>
    ///         ⚠️ Empty when the entity's syntax is not in this compilation: across assemblies the body
    ///         cannot be read, so "reads nothing" is indistinguishable from "cannot tell" and the safe
    ///         reading is the second.
    ///     </para>
    /// </remarks>
    public EquatableArray<InvariantModel> CheckableInvariants { get; init; } =
        EquatableArray<InvariantModel>.Empty;
    public required string FieldName { get; init; }
    public required string RepositoryFieldName { get; init; }
    public required string RepositoryTypeFullName { get; init; }
}
