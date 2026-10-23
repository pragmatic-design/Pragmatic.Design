namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Names the engine that persists the entities of the <c>[Boundary]</c> it marks.
/// </summary>
/// <remarks>
///     <para>
///         An entity's boundary is the one the generator already finds: its <c>[BelongsTo&lt;T&gt;]</c>, else the
///         boundary that claims it with <c>[Owns&lt;T&gt;]</c>, else the only <c>[Boundary]</c> of its assembly.
///     </para>
///     <para>
///         Optional while the compilation references one engine: that one is the default. With two or more referenced,
///         an entity whose boundary names none is <c>PRAG0743</c>; a boundary naming an engine the compilation does not
///         reference is <c>PRAG0744</c>.
///     </para>
///     <example>
///         <code>
/// [Boundary]
/// [PersistenceEngine(PersistenceEngineKind.EfCore)]
/// public partial class BillingBoundary;
/// </code>
///     </example>
/// </remarks>
/// <param name="engine">The engine of the boundary's entities.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PersistenceEngineAttribute(PersistenceEngineKind engine) : Attribute
{
    /// <summary>The engine of the boundary's entities.</summary>
    public PersistenceEngineKind Engine { get; } = engine;
}
