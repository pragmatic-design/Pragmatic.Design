namespace Pragmatic.Persistence.RollUp;

/// <summary>
///     A roll-up maintenance rule (#2): how a single changed child adjusts its parent's stored aggregate. Pure
///     (no EF dependency) so the SG can emit it into the module assembly, where it can reach the entity's
///     generated internal apply method. The EF interceptor reads the change state and drives these accessors —
///     all backed by typed delegates in <see cref="RollUpRule{TChild, TParent}"/>, so there is no reflection.
/// </summary>
public abstract class RollUpRule
{
    /// <summary>
    ///     The parent property holding the aggregate (e.g. <c>Subtotal</c>). The interceptor uses it to
    ///     emit a relational <c>SET col = col + delta</c> (lost-update-safe under concurrency) and to
    ///     exclude the property from EF's own UPDATE when the parent is tracked.
    /// </summary>
    public required string AggregatePropertyName { get; init; }

    /// <summary>The child entity type this rule reacts to.</summary>
    public abstract Type ChildType { get; }

    /// <summary>The parent entity type that owns the aggregate.</summary>
    public abstract Type ParentType { get; }

    /// <summary>Reads the parent's key (the child's foreign key) from a child instance.</summary>
    public abstract object GetParentKey(object child);

    /// <summary>Reads the aggregated amount from a child instance.</summary>
    public abstract decimal GetAmount(object child);

    /// <summary>Applies the signed delta to the parent's stored aggregate.</summary>
    public abstract void ApplyDelta(object parent, decimal delta);
}
