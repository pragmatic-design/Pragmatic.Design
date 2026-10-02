using Pragmatic.SourceGen;

namespace Pragmatic.Testing.SourceGenerator.Models;

/// <summary>One create endpoint's data for a CRUD round-trip test (#7, phase 2): route, permission and the synthesized body fields.</summary>
internal sealed record CrudCreateModel
{
    public required string Boundary { get; init; }

    /// <summary>
    ///     The create <b>operation's</b> name — the endpoint's action name, e.g.
    ///     <c>CreateDraftInvoiceMutation</c>. It is the key every contract that arranges through this
    ///     endpoint asks the application under, through <c>PragmaticContractHost.Body</c> and
    ///     <c>Prepare</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It was called <c>EntityName</c> and it never held one — <see cref="EntityTypeName" /> does.
    ///     That is how the transition contract came to ask for the same create under
    ///     <c>"Create" + entity.Name</c> while the CRUD one asked under the action's: the field with the
    ///     right-sounding name held the wrong thing, so the other half went and read the entity itself.
    /// </remarks>
    public required string OperationName { get; init; }

    public required string Route { get; init; }
    public required string? Permission { get; init; }
    public required EquatableArray<CrudFieldModel> Fields { get; init; }

    /// <summary>True when the created entity is tenant-scoped, so a cross-tenant isolation test is emitted.</summary>
    public bool IsTenantScoped { get; init; }

    /// <summary>
    ///     The permission of the entity's read by id — the GET on <c>{Route}/{param}</c> the create's Location
    ///     points at — or null when there is none, or it asks for nothing.
    /// </summary>
    /// <remarks>
    ///     The isolation test's tenant-B reader holds it. Without it that read is refused 403 before any
    ///     lookup, and the 404 the test asserts is never reached.
    /// </remarks>
    public string? ReadPermission { get; init; }

    /// <summary>The repository entity type name (e.g. <c>Invoice</c>), used to correlate transition endpoints to their create endpoint.</summary>
    public string? EntityTypeName { get; init; }

    /// <summary>
    ///     True when every request-body field could be synthesized to a concrete value. When false the body has
    ///     a complex/FK/strongly-typed field we cannot fill, so the success and tenant-isolation tests are
    ///     skipped (they would POST an invalid body); only the validation-400 test (which POSTs <c>{}</c>) is emitted.
    /// </summary>
    public bool CanSynthesizeBody { get; init; }

    /// <summary>
    ///     True when the create is a <c>DomainAction</c> rather than a <c>Mutation&lt;TEntity&gt;</c>.
    ///     <para>
    ///         Such a create does not get a CRUD round-trip contract of its own: a command can have business
    ///         preconditions a synthesized body will not meet, so asserting 2xx on it would be a test that
    ///         fails for the wrong reason. It is still recorded, because a state-transition test needs
    ///         <i>some</i> way to bring the entity into its initial state — and for an entity with a state
    ///         machine, a command is usually the only way it is ever created.
    ///     </para>
    /// </summary>
    public bool IsDomainActionCreate { get; init; }
}

/// <summary>A request-body field and the C# expression producing its synthesized value.</summary>
internal sealed record CrudFieldModel
{
    public required string Name { get; init; }
    public required string ValueExpression { get; init; }
}
