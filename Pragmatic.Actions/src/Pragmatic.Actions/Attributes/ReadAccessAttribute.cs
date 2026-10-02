// Pragmatic.Actions - ReadAccess Attribute

namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Declares that a boundary needs read access to entities from another boundary
///     via SQL join (same physical database required).
/// </summary>
/// <typeparam name="TEntity">The entity type to read from the other boundary.</typeparam>
/// <remarks>
///     <para>
///         When a boundary needs to JOIN to entities from another boundary's table
///         (e.g., for read-optimized queries), declare it with <c>[ReadAccess&lt;TEntity&gt;]</c>.
///     </para>
///     <para>
///         The Persistence.EFCore source generator will add a <c>DbSet&lt;TEntity&gt;</c>
///         to this boundary's DbContext with <c>ExcludeFromMigrations()</c>,
///         so migrations don't create duplicate tables.
///     </para>
///     <para>
///         The Composition source generator validates at compile-time that both boundaries
///         are wired to the same physical database (PRAG0601/PRAG0602).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // BookingBoundary needs to read Properties from Catalog
/// [Boundary]
/// [ReadAccess&lt;Property&gt;]
/// public class BookingBoundary : IBoundary { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ReadAccessAttribute<TEntity> : Attribute
    where TEntity : class
{
}
