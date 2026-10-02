namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares a stored aggregate the parent keeps over its <typeparamref name="TChild"/> children (#2). The
///     decorated property holds the roll-up (e.g. an invoice <c>Subtotal</c> = sum of its lines'
///     <c>Amount</c>); the framework keeps it current as children are created and deleted, in the child's own
///     unit-of-work — no manual recompute. <c>[RollUp&lt;Line&gt;(nameof(Line.Amount))] decimal Subtotal { get; }</c>.
/// </summary>
/// <typeparam name="TChild">The child entity type whose property is aggregated.</typeparam>
/// <remarks>
///     <para>
///         Both aggregations are generated. <see cref="RollUpAggregation.Sum" /> reads the named child
///         property; <see cref="RollUpAggregation.Count" /> reads none — each child is worth one — and
///         the delta is converted into the aggregate's own type, because the maintenance pipeline
///         carries <c>decimal</c> for every rule and a count lives in an <c>int</c>. For a count pass
///         the child property as an empty string: it is ignored, and naming one could put a
///         non-numeric member where the rule expects a number.
///     </para>
///     <para>
///         The rules reach the container the way every generated registration does: the module
///         publishes them and the host calls what it finds on its references. An application that wires
///         persistence by hand with <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c> gets
///         the same rules through that entry point, and registering both ways does not double them.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class RollUpAttribute<TChild> : Attribute
    where TChild : class
{
    /// <param name="childProperty">The child property aggregated (ignored for <see cref="RollUpAggregation.Count"/>).</param>
    /// <param name="aggregation">How children are aggregated. Defaults to <see cref="RollUpAggregation.Sum"/>.</param>
    public RollUpAttribute(string childProperty, RollUpAggregation aggregation = RollUpAggregation.Sum)
    {
        ChildProperty = childProperty;
        Aggregation = aggregation;
    }

    /// <summary>The child property aggregated onto the parent.</summary>
    public string ChildProperty { get; }

    /// <summary>The aggregation kind.</summary>
    public RollUpAggregation Aggregation { get; }
}
