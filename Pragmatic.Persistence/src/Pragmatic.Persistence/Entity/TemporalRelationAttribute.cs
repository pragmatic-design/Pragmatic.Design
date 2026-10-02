namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as a temporal relation — a relationship with a validity period.
///     The entity must implement <see cref="Pragmatic.Persistence.Entity.ITemporalRelation"/>.
/// </summary>
/// <remarks>
///     <para>
///         The source generator will automatically:
///         <list type="bullet">
///             <item><description>Generate <c>Active()</c> and <c>ActiveAt(date)</c> query extension methods</description></item>
///             <item><description>Add temporal filter to the FilterMap (only active records by default)</description></item>
///             <item><description>Generate <c>ValidateTemporalConstraints()</c> when <see cref="MaxActive"/> is set</description></item>
///             <item><description>Generate <c>AutoClosePrevious()</c> when <see cref="MaxActive"/> is 1</description></item>
///         </list>
///     </para>
///     <para>
///         <b>Enforcement lives in the mutation pipeline, not in <c>SaveChanges</c>.</b> A create
///         mutation targeting the entity calls <c>ValidateTemporalConstraints()</c> before persisting
///         and rejects a breach. <c>AutoClosePrevious()</c> is a helper your operation must call itself
///         (e.g. in a mutation's <c>ApplyAsync</c>). Outside a mutation, a plain <c>Add</c> +
///         <c>SaveChanges</c> neither rejects a <see cref="MaxActive"/> violation nor auto-closes the
///         previous active period.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Entity]
/// [TemporalRelation(MaxActive = 1)]
/// public partial class UserRole : ITemporalRelation
/// {
///     public DateTimeOffset ValidFrom { get; set; }
///     public DateTimeOffset? ValidTo { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemporalRelationAttribute : Attribute
{
    /// <summary>
    ///     Maximum number of active relations allowed simultaneously.
    ///     0 = no limit (default). 1 = only one active at a time.
    /// </summary>
    public int MaxActive { get; set; }

    /// <summary>
    ///     Whether overlapping validity periods are allowed.
    ///     Default: false — ValidTo of previous must be &lt;= ValidFrom of new.
    /// </summary>
    public bool AllowOverlap { get; set; }
}
