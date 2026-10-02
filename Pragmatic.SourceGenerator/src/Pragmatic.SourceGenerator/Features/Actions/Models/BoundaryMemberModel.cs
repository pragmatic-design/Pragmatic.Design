using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Unified model for boundary interface members (both DomainAction and Mutation).
/// </summary>
internal sealed record BoundaryMemberModel
{
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string Namespace { get; init; }
    public bool IsMutation { get; init; }

    /// <summary>
    ///     The mutation's mode is <c>Create</c>, so it has no row to be handed.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Creating is what that mode does: supplying an entity contradicts it, and the invoker
    ///     throws rather than quietly updating instead. The preloaded overload is therefore not
    ///     published for one — a boundary offering a call whose only outcome is that throw would be
    ///     worse than not offering it.
    /// </remarks>
    public bool MutationCreates { get; init; }

    /// <summary>Whether this member is a declared query rather than a write.</summary>
    /// <remarks>
    ///     It changes what the implementation invokes: a query's invoker is a nested type the same
    ///     generator writes, constructed from the provider, not an <c>IDomainActionInvoker</c> resolved
    ///     from DI. Everything else — the signature, the unwrapped overload, the internal/public split —
    ///     is the same, because a read is an operation like any other.
    /// </remarks>
    public bool IsQuery { get; init; }
    public bool IsVoid { get; init; }
    public bool IsInternal { get; init; }
    public string? ReturnTypeName { get; init; }
    public string? EntityFullTypeName { get; init; }

    /// <summary>
    ///     For a mutation whose <c>ReturnType</c> is not the entity: the lambda that turns the entity its
    ///     invoker returns into <see cref="ReturnTypeName" />. <c>null</c> when the member returns what the
    ///     invoker returns.
    /// </summary>
    public string? MutationResultProjection { get; init; }

    public string? BelongsToTypeName { get; init; }

    /// <summary>Explicit sub-boundary name (e.g. "ReservationComments"). Overrides namespace inference.</summary>
    public string? SubBoundaryName { get; init; }

    /// <summary>
    ///     What the operation wrote in <c>[SubBoundary(Name = …)]</c>, verbatim, or null when it wrote
    ///     none.
    /// </summary>
    /// <remarks>
    ///     Kept beside <see cref="SubBoundaryName" /> rather than folded into it, because the boundary
    ///     pass has to tell "declared nothing" from "declared an empty name": the first falls back to
    ///     the namespace, the second is PRAG0416. A generated operation — <c>[Resource]</c>, a trait —
    ///     sets only <see cref="SubBoundaryName" />, having declared nothing.
    /// </remarks>
    public string? DeclaredSubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Description = …)]</c> says: the group interface's summary.</summary>
    public string? SubBoundaryDescription { get; init; }

    /// <summary>
    ///     Where the operation is declared, so a diagnostic about the group its namespace produced can
    ///     point at it. Excluded from equality by <see cref="LocationInfo" /> itself.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    /// <summary>
    ///     Whether this member declared <c>[UndoWith&lt;T&gt;]</c>. Carried up to the facade so a
    ///     caller in another boundary can be told, by the marker, that every step it can reach undoes
    ///     itself — which is what lets PRAG0424 stay quiet for someone who did the work.
    /// </summary>
    public bool IsCompensable { get; init; }

    public EquatableArray<ActionPropertyModel> InputProperties { get; init; } = EquatableArray<ActionPropertyModel>.Empty;
    public bool HasInputProperties => !InputProperties.IsDefaultOrEmpty;

    public static BoundaryMemberModel FromAction(ActionModel action) => new()
    {
        TypeName = action.TypeName,
        FullTypeName = action.FullTypeName,
        Namespace = action.Namespace,
        IsMutation = false,
        IsVoid = action.IsVoid,
        IsInternal = action.IsInternal,
        ReturnTypeName = action.ReturnTypeName,
        EntityFullTypeName = null,
        BelongsToTypeName = action.BelongsToTypeName,
        SubBoundaryName = action.SubBoundaryName,
        DeclaredSubBoundaryName = action.DeclaredSubBoundaryName,
        SubBoundaryDescription = action.SubBoundaryDescription,
        LocationInfo = action.LocationInfo,
        IsCompensable = action.CompensatorTypeName is not null,
        InputProperties = UnboundInputs(action.InputProperties)
    };

    /// <summary>
    ///     A declared query, as a member of its boundary.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>SubBoundaryName</c> is normally null: the group is inferred from the namespace by
    ///         <c>SubBoundaryTransform</c>, in the same pass that infers an action's, so a query lands
    ///         in the group its neighbours are in without a second rule. A <b>scaffolded</b> query is
    ///         the exception — it borrows the entity's namespace, which is flat by rule — and it says
    ///         its group here instead.
    ///     </para>
    ///     <para>
    ///         The answer type is the query's shape — one row, a page, or a list — because that is what
    ///         the caller reads. It has to agree with the invoker's <c>RunAsync</c>, which is written
    ///         from the same model.
    ///     </para>
    /// </remarks>
    public static BoundaryMemberModel FromQuery(Persistence.Models.QueryModel query) => new()
    {
        TypeName = query.TypeName,
        FullTypeName = $"global::{query.Namespace}.{query.TypeName}",
        Namespace = query.Namespace,
        IsMutation = false,
        IsQuery = true,
        IsVoid = false,
        // A query is never internal-only: what it answers is decided by its permission, not by which
        // interface names it. The internal facade still exists and still enters the internal call.
        IsInternal = false,
        ReturnTypeName = QueryAnswerType(query),
        EntityFullTypeName = $"global::{query.EntityTypeFullName}",
        BelongsToTypeName = query.BoundaryTypeName,
        SubBoundaryName = query.SubBoundaryName,
        DeclaredSubBoundaryName = query.DeclaredSubBoundaryName,
        SubBoundaryDescription = query.SubBoundaryDescription,
        LocationInfo = query.Location,
        IsCompensable = false,
        // [FromCurrentUser] is the invoker's to fill: an overload taking it would be a caller choosing
        // whose data is read — and would not compile, since only the nested invoker reaches the setter.
        InputProperties = query.Properties
            .Where(static p => !p.IsInertInput && !p.IsBoundFromTheCaller)
            .Select(static p => new ActionPropertyModel
            {
                Name = p.PropertyName,
                // Qualified: this signature is emitted into the boundary's file, which has none of the
                // author's using directives.
                TypeName = p.QualifiedPropertyType ?? p.PropertyType,
                IsRequired = p.IsRequired,
                IsNullable = p.IsNullable,
            })
            .ToImmutableArray()
    };

    /// <summary>What a caller passes: every input but the ones the invoker writes.</summary>
    /// <remarks>
    ///     The same rule the query's overload applies above. A <c>[FromClock]</c> or
    ///     <c>[FromCurrentUser]</c> parameter would be a caller choosing the day or the user — and would
    ///     not compile, since only the nested invoker reaches the setter.
    /// </remarks>
    private static EquatableArray<ActionPropertyModel> UnboundInputs(EquatableArray<ActionPropertyModel> inputs)
        => inputs.Any(static p => p.IsBoundByTheInvoker)
            ? inputs.Where(static p => !p.IsBoundByTheInvoker).ToImmutableArray()
            : inputs;

    /// <summary>What a mutation's member returns: its <c>ReturnType</c>, not always the entity.</summary>
    public static string MutationReturnTypeName(MutationModel mutation)
        => MutationReturnTypeName(mutation.EffectiveReturnType, mutation.FullTypeName, mutation.EntityFullTypeName);

    /// <summary>
    ///     The same answer from its parts, for a package's mutation known only through its metadata.
    /// </summary>
    /// <remarks>The key of every entity is a <c>Guid</c>: <c>IEntity.PersistenceId</c>.</remarks>
    public static string MutationReturnTypeName(
        MutationReturnTypeValue returnType, string mutationFullTypeName, string entityFullTypeName) => returnType switch
    {
        MutationReturnTypeValue.Id => "global::System.Guid",
        MutationReturnTypeValue.LogicalKey => $"{mutationFullTypeName}.{Templates.MutationLogicalKeyTemplate.RecordName}",
        _ => entityFullTypeName
    };

    /// <summary>The projection from the entity the invoker returns, or <c>null</c> for the entity itself.</summary>
    /// <remarks>
    ///     <c>PersistenceId</c>, not the <c>Id</c> alias: <c>IEntity</c> guarantees the first, and the
    ///     alias is generated only on an <c>[Entity]</c>.
    /// </remarks>
    private static string? MutationResultProjectionOf(MutationReturnTypeValue returnType, string mutationFullTypeName)
        => returnType switch
        {
            MutationReturnTypeValue.Id => "static __entity => __entity.PersistenceId",
            MutationReturnTypeValue.LogicalKey =>
                $"static __entity => {mutationFullTypeName}.{Templates.MutationLogicalKeyTemplate.RecordName}.From(__entity)",
            _ => null
        };

    private static MutationReturnTypeValue ParseReturnKind(string? returnKind)
        => returnKind is not null && System.Enum.TryParse<MutationReturnTypeValue>(returnKind, out var kind)
            ? kind
            : MutationReturnTypeValue.Entity;

    /// <summary>What the query answers, in the shape its invoker returns.</summary>
    private static string QueryAnswerType(Persistence.Models.QueryModel query)
    {
        var result = $"global::{query.ResultTypeFullName}";

        return query switch
        {
            { IsSingle: true } => result,
            { IsPaged: true } => $"global::Pragmatic.Persistence.Query.Results.PagedResult<{result}>",
            _ => $"global::System.Collections.Generic.IReadOnlyList<{result}>"
        };
    }

    public static BoundaryMemberModel FromMutation(MutationModel mutation) => new()
    {
        TypeName = mutation.TypeName,
        FullTypeName = mutation.FullTypeName,
        Namespace = mutation.Namespace,
        IsMutation = true,
        MutationCreates = mutation.Mode == MutationModeValue.Create,
        IsVoid = false,
        IsInternal = mutation.IsInternal,
        ReturnTypeName = MutationReturnTypeName(mutation),
        MutationResultProjection = MutationResultProjectionOf(mutation.EffectiveReturnType, mutation.FullTypeName),
        EntityFullTypeName = mutation.EntityFullTypeName,
        BelongsToTypeName = mutation.BelongsToTypeName,
        SubBoundaryName = mutation.SubBoundaryName,
        DeclaredSubBoundaryName = mutation.DeclaredSubBoundaryName,
        SubBoundaryDescription = mutation.SubBoundaryDescription,
        LocationInfo = mutation.LocationInfo,
        IsCompensable = mutation.CompensatorTypeName is not null,
        InputProperties = UnboundInputs(mutation.InputProperties)
    };

    /// <summary>
    ///     Creates a BoundaryMemberModel from a package action registration.
    ///     Used to generate sub-boundary interface methods for package actions.
    /// </summary>
    public static BoundaryMemberModel FromPackageRegistration(PackageActionRegistration reg)
    {
        // Extract simple type name from FQN (e.g., "global::Pragmatic.Identity.Local.Actions.LoginUser" → "LoginUser")
        var fullType = reg.ActionType;
        var simpleTypeName = fullType;
        var lastDot = fullType.LastIndexOf('.');
        if (lastDot >= 0) simpleTypeName = fullType.Substring(lastDot + 1);

        // Extract namespace
        var ns = lastDot >= 0 ? fullType.Substring(0, lastDot) : "";
        if (ns.StartsWith("global::", StringComparison.Ordinal))
            ns = ns.Substring(8);

        return new BoundaryMemberModel
        {
            TypeName = simpleTypeName,
            FullTypeName = fullType,
            Namespace = ns,
            IsMutation = reg.IsMutation,
            IsVoid = reg.IsVoid,
            IsInternal = false,
            ReturnTypeName = reg is { IsMutation: true, EntityType: not null }
                ? MutationReturnTypeName(ParseReturnKind(reg.MutationReturnKind), fullType, reg.EntityType)
                : reg.ReturnType,
            MutationResultProjection = reg.IsMutation
                ? MutationResultProjectionOf(ParseReturnKind(reg.MutationReturnKind), fullType)
                : null,
            EntityFullTypeName = reg.EntityType,
        };
    }
}
