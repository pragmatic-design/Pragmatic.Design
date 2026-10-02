namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One <c>[RollUp&lt;TChild&gt;]</c> on a parent property (#2): the data to emit the parent's internal apply
///     method and register a typed <c>RollUpRule</c>. Type names are global-qualified.
/// </summary>
internal sealed record RollUpModel
{
    public required string ParentFullName { get; init; }
    public required string ParentShortName { get; init; }
    public required string ParentNamespace { get; init; }

    /// <summary>The parent property holding the aggregate (e.g. <c>Subtotal</c>).</summary>
    public required string RollupProperty { get; init; }

    public required string ChildFullName { get; init; }
    public required string ChildShortName { get; init; }

    /// <summary>The child property aggregated (e.g. <c>Amount</c>).</summary>
    public required string ChildAmountProperty { get; init; }

    /// <summary>The child's foreign-key property pointing to the parent (convention <c>{Parent}Id</c>).</summary>
    public required string ChildForeignKeyProperty { get; init; }

    /// <summary>
    ///     Whether the aggregation counts children rather than summing a property of theirs.
    /// </summary>
    /// <remarks>
    ///     The enum had two members and this transform read neither, so <c>Count</c> emitted the same
    ///     <c>Amount = c =&gt; c.{childProperty}</c> as a sum — over a property the attribute documents
    ///     as ignored, which is how counting rows produced a <c>CS0029</c> in the registration.
    /// </remarks>
    public bool IsCount { get; init; }

    /// <summary>
    ///     The aggregate property's own type, so the apply method can land the delta in it.
    /// </summary>
    /// <remarks>
    ///     The roll-up pipeline carries <c>decimal</c> end to end — one numeric type for every rule,
    ///     rather than a generic parameter threaded through the interceptor. A count lives in an
    ///     <c>int</c>, so the conversion happens at the one place that knows both: the generated apply
    ///     method on the parent. Without it, <c>MentionCount += delta</c> is a <c>CS0266</c>.
    /// </remarks>
    public required string RollupPropertyTypeName { get; init; }

    /// <remarks>
    ///     A count needs no child property. Requiring one would force an author to name a member the
    ///     attribute documents as ignored — which would then reach the rule as
    ///     <c>Amount = c =&gt; c.SomeString</c> — and a <c>Count</c> declared with an empty name would
    ///     make the whole roll-up invalid, so the feature would emit nothing at all: no registration,
    ///     no apply method, and no error either.
    /// </remarks>
    public bool IsValid =>
        !string.IsNullOrEmpty(ParentFullName) &&
        !string.IsNullOrEmpty(RollupProperty) &&
        !string.IsNullOrEmpty(ChildFullName) &&
        (IsCount || !string.IsNullOrEmpty(ChildAmountProperty));
}
