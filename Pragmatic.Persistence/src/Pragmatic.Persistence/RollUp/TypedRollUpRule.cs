namespace Pragmatic.Persistence.RollUp;

/// <summary>
///     Typed roll-up rule: maintains a <typeparamref name="TParent"/> aggregate from its
///     <typeparamref name="TChild"/> children via typed delegates (the SG emits them), so maintenance is
///     reflection-free. The object-typed base members just downcast to these.
/// </summary>
/// <typeparam name="TChild">The child entity type.</typeparam>
/// <typeparam name="TParent">The parent entity that owns the aggregate.</typeparam>
public sealed class RollUpRule<TChild, TParent> : RollUpRule
    where TChild : class
    where TParent : class
{
    /// <summary>Reads the parent's primary-key value from the child (its foreign key).</summary>
    public required Func<TChild, object> ParentKey { get; init; }

    /// <summary>Reads the aggregated amount from the child.</summary>
    public required Func<TChild, decimal> Amount { get; init; }

    /// <summary>Applies the signed delta to the parent's stored aggregate property.</summary>
    public required Action<TParent, decimal> ApplyToParent { get; init; }

    /// <inheritdoc />
    public override Type ChildType => typeof(TChild);

    /// <inheritdoc />
    public override Type ParentType => typeof(TParent);

    /// <inheritdoc />
    public override object GetParentKey(object child) => ParentKey((TChild)child);

    /// <inheritdoc />
    public override decimal GetAmount(object child) => Amount((TChild)child);

    /// <inheritdoc />
    public override void ApplyDelta(object parent, decimal delta) => ApplyToParent((TParent)parent, delta);
}
