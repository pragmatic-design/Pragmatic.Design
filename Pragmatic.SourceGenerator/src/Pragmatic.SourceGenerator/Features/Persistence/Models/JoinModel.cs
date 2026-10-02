namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a join declaration on a Query class.
/// </summary>
internal sealed record JoinModel
{
    /// <summary>
    ///     The fully qualified target entity type name.
    /// </summary>
    public required string TargetTypeFullName { get; init; }

    /// <summary>
    ///     The simple target entity type name.
    /// </summary>
    public required string TargetTypeName { get; init; }

    /// <summary>
    ///     The navigation property path (e.g., "Customer" or "Lines.Product").
    /// </summary>
    public string? Via { get; init; }

    /// <summary>
    ///     The foreign key property name on the source entity.
    /// </summary>
    public string? ForeignKey { get; init; }

    /// <summary>
    ///     The key property name on the target entity.
    /// </summary>
    public string? TargetKey { get; init; }

    /// <summary>
    ///     The type of join (0 = Inner, 1 = Left, 2 = Right, 3 = Full, 4 = Cross).
    /// </summary>
    public JoinTypeKind JoinType { get; init; } = JoinTypeKind.Inner;

    /// <summary>
    ///     Optional alias for referencing this join.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    ///     Whether this join is to a collection navigation.
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    ///     The first segment of <see cref="Via" /> that is not a navigation of the query's entity, or
    ///     <c>null</c> when every segment resolves.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Copied into the generated <c>Apply</c> unread, a <c>Via</c> name that is not a
    ///     navigation would be a <c>CS1061</c> inside a <c>.g.cs</c> — at a line of a file the author
    ///     did not write, for a string they wrote on an attribute. PRAG0737 answers it where they can
    ///     see it, through the same resolver <c>[EagerLoad]</c> uses.
    /// </remarks>
    public string? UnresolvedSegment { get; init; }

    /// <summary>
    ///     Whether this join uses navigation-based configuration (Via specified).
    /// </summary>
    public bool IsNavigationJoin => !string.IsNullOrEmpty(Via);

    /// <summary>
    ///     A navigation join whose path resolves — the only kind that contributes an include path.
    /// </summary>
    public bool IsResolvedNavigationJoin => IsNavigationJoin && UnresolvedSegment is null;

    /// <summary>
    ///     <see cref="ForeignKey" /> when it names no property of the query's entity, otherwise
    ///     <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The same rule <c>Via</c> follows, for the same reason: it is a string the author
    ///     wrote on an attribute, and the alternative is a <c>CS1061</c> at a line of a generated file
    ///     they never saw.
    /// </remarks>
    public string? UnresolvedForeignKey { get; init; }

    /// <summary>
    ///     <see cref="TargetKey" /> when it names no property of the joined type, otherwise <c>null</c>.
    /// </summary>
    public string? UnresolvedTargetKey { get; init; }

    /// <summary>
    ///     The fully qualified type of <see cref="ForeignKey" /> on the query's entity.
    /// </summary>
    public string? ForeignKeyTypeFullName { get; init; }

    /// <summary>
    ///     The fully qualified type of <see cref="TargetKey" /> on the joined type.
    /// </summary>
    public string? TargetKeyTypeFullName { get; init; }

    /// <summary>
    ///     The member the generated join names on the query's entity for <see cref="ForeignKey" />.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not always the same string. <c>Id</c> on an <c>[Entity]</c> is an unmapped alias for
    ///     <c>PersistenceId</c>, and an expression naming it is one EF Core cannot translate — so the
    ///     author writes <c>Id</c>, which is what they see, and the generated file writes the column.
    /// </remarks>
    public string? ForeignKeyMember { get; init; }

    /// <summary>The member the generated join names on the joined type for <see cref="TargetKey" />.</summary>
    public string? TargetKeyMember { get; init; }

    /// <summary>
    ///     Whether the target is in the model of the boundary this query reads from.
    /// </summary>
    /// <remarks>
    ///     ⚠️ False means the join cannot be made at all: EF Core composes one only inside a single
    ///     <c>DbContext</c> instance, and a host builds one per boundary. Reported as <c>PRAG0742</c>,
    ///     naming the <c>[ReadAccess&lt;T&gt;]</c> that would put the target there.
    /// </remarks>
    public bool TargetIsReachable { get; init; } = true;

    /// <summary>
    ///     Whether this join uses explicit key configuration.
    /// </summary>
    public bool IsKeyJoin => !string.IsNullOrEmpty(ForeignKey);

    /// <summary>
    ///     A key join whose three names all resolve — the only kind that generates a join.
    /// </summary>
    public bool IsResolvedKeyJoin =>
        IsKeyJoin && UnresolvedForeignKey is null && UnresolvedTargetKey is null && TargetIsReachable;

    /// <summary>
    ///     Whether rows of the root survive when the target has no match.
    /// </summary>
    /// <remarks>
    ///     The whole of <see cref="JoinType" /> at the root: <c>Left</c> keeps them, <c>Inner</c> does
    ///     not. <c>Cross</c> pairs every row with every row and so keeps none unmatched by definition.
    /// </remarks>
    public bool KeepsUnmatchedRows => JoinType == JoinTypeKind.Left;

    /// <summary>
    ///     Whether EF Core can translate this join type at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Full</c> has no LINQ spelling that EF Core turns into a FULL OUTER JOIN. <c>Right</c>
    ///     is the same shape with the operands swapped, and that is what refuses it here: the step
    ///     receives the root set the executor has <b>already filtered, sorted and paged</b>, so rows of
    ///     the target that the root's filters never selected cannot be added back without discarding
    ///     all of it. Generating an inner join for either would be a join that is not one, wearing a
    ///     different name.
    /// </remarks>
    public bool IsGeneratableJoinType =>
        JoinType is JoinTypeKind.Inner or JoinTypeKind.Left or JoinTypeKind.Cross;

    /// <summary>
    ///     Gets the effective alias (alias if specified, otherwise target type name).
    /// </summary>
    public string EffectiveAlias => Alias ?? TargetTypeName;
}

/// <summary>
///     Join type kinds (mirrors JoinType enum).
/// </summary>
internal enum JoinTypeKind
{
    Inner = 0,
    Left = 1,
    Right = 2,
    Full = 3,
    Cross = 4
}
