using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A query derived from a specification: the type the generator writes so a rule already expressed
///     once becomes readable.
/// </summary>
/// <remarks>
///     <para>
///         The specification is <b>not</b> modified. Adding <c>Page</c>/<c>PageSize</c> to it would break
///         what makes it worth having — <c>Confirmed() &amp; OfKind(x)</c> has no answer for which page
///         it is on — so a new type holds the paging, the route and the projection, and holds the
///         specification as a property.
///     </para>
///     <para>
///         That property is the whole join with the existing machinery: a query already contributes any
///         <c>Specification&lt;TEntity&gt;</c> property of its own to <c>Apply()</c> and to
///         <c>ToSpecification()</c>, recognised by type. The derived type declares one, so the templates
///         that render a hand-written query render this one unchanged — which is the test of whether the
///         derivation is right.
///     </para>
/// </remarks>
internal sealed record DerivedQueryModel
{
    /// <summary>The namespace of the type that declares the specification.</summary>
    public required string Namespace { get; init; }

    /// <summary>The derived type's name, <c>{Specification}Query</c>.</summary>
    public required string TypeName { get; init; }

    /// <summary>The entity, fully qualified.</summary>
    public required string EntityTypeFullName { get; init; }

    /// <summary>The entity's simple name.</summary>
    public required string EntityTypeName { get; init; }

    /// <summary>What the query answers with, fully qualified — the entity itself for the one-arg form.</summary>
    public required string ResultTypeFullName { get; init; }

    /// <summary>The result's simple name.</summary>
    public required string ResultTypeName { get; init; }

    /// <summary>The declaring type of the specification, fully qualified: the derived query calls it.</summary>
    public required string ContainerFullTypeName { get; init; }

    /// <summary>The specification's own member name.</summary>
    public required string MemberName { get; init; }

    /// <summary>A property has no argument list; a method may have one.</summary>
    public required bool IsProperty { get; init; }

    /// <summary>Whether the paging surface is generated.</summary>
    public required bool Paged { get; init; }

    /// <summary>Whether the query answers at most one row.</summary>
    public required bool Single { get; init; }

    /// <summary>The specification's parameters, which become the query's inputs.</summary>
    public EquatableArray<DerivedQueryInput> Inputs { get; init; } = EquatableArray<DerivedQueryInput>.Empty;

    /// <summary>Where the attribute sits, for a diagnostic that has to name it.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>
    ///     The route the specification declares with <c>[Endpoint]</c>, or null when it declares none.
    /// </summary>
    /// <remarks>
    ///     Promotion to a <b>route</b> is opt-in on top of promotion to a query: a rule that says nothing
    ///     about HTTP stays executable in-process and publishes nothing. Deriving a path from the member's
    ///     name would put a URL nobody chose into the contract.
    /// </remarks>
    public string? Route { get; init; }

    /// <summary>The verb the <c>[Endpoint]</c> declares, as the endpoint model spells it ("Get").</summary>
    public string? HttpMethod { get; init; }

    /// <summary>The permissions the specification declares with <c>[RequirePermission]</c>.</summary>
    public EquatableArray<string> Permissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The permission constants the declaration names and this compilation cannot bind yet.
    /// </summary>
    /// <remarks>
    ///     <c>[RequirePermission(BookingPermissions.Reservation.Read)]</c> names a constant this same
    ///     generator writes, so while the compilation is being analysed there is no value to read — only
    ///     a path. The endpoint feature resolves it against the permission catalog once every producer
    ///     has contributed. Dropping it would map the route with no authorization at all.
    /// </remarks>
    public EquatableArray<string> UnresolvedPermissionPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Whether the declaration says the route needs no authentication.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>
    ///     The boundary that owns the entity, fully qualified, or null when nothing says.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The generated handler resolves its <c>DbContext</c> <b>keyed</b> by this type. Getting it
    ///     wrong is not a build error: it resolves another boundary's context, and the first request
    ///     answers 500.
    /// </remarks>
    public string? BoundaryFullTypeName { get; init; }

    /// <summary>Whether the specification declares a route at all.</summary>
    public bool HasRoute => !string.IsNullOrEmpty(Route);

    /// <summary>The name the specification property carries on the derived type.</summary>
    public const string SpecificationPropertyName = "Rule";

    /// <summary>Everything needed to write the type is present.</summary>
    /// <summary>Why nothing was derived, when nothing was. <see cref="DerivedQueryRejection.None" /> otherwise.</summary>
    /// <remarks>
    ///     The transform is total: it answers with a model either way, so a declaration it declines can
    ///     be reported instead of disappearing. <c>PRAG0729</c> is where it is said.
    /// </remarks>
    public DerivedQueryRejection Rejection { get; init; }

    /// <summary>The member the attribute was written on, even when nothing was derived from it.</summary>
    public string DeclaredOn { get; init; } = "";

    public bool IsValid =>
        Rejection == DerivedQueryRejection.None
        && !string.IsNullOrEmpty(TypeName)
        && !string.IsNullOrEmpty(EntityTypeFullName)
        && !string.IsNullOrEmpty(ContainerFullTypeName)
        && !string.IsNullOrEmpty(MemberName);
}
