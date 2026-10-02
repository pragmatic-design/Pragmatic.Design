namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as soft-deletable instead of being permanently deleted.
/// </summary>
/// <remarks>
///     <para>
///         The entity must implement <c>ISoftDelete</c> which provides:
///         <c>IsDeleted</c> (bool), <c>DeletedAt</c> (DateTimeOffset?), <c>DeletedBy</c> (string?).
///     </para>
///     <para>
///         The source generator will automatically:
///         <list type="bullet">
///             <item><description>Generate a nested <c>{TypeName}.SoftDeleteFilter</c> implementing IQueryFilter&lt;T&gt;</description></item>
///             <item><description>Add EF Core named query filter as safety net</description></item>
///             <item><description>Initialize <c>IsDeleted = false</c> in Create factory method</description></item>
///         </list>
///     </para>
///     <para>
///         Delete mutations on soft-delete entities set <c>IsDeleted = true</c>,
///         <c>DeletedAt = UtcNow</c>, and <c>DeletedBy = currentUser</c> instead of removing the record.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SoftDeleteAttribute : Attribute
{
    /// <summary>
    ///     When true, soft-deleting this entity will also soft-delete related entities
    ///     marked with <c>[SoftDelete]</c> via navigation properties.
    ///     Default: false (only the target entity is soft-deleted).
    /// </summary>
    public bool Cascade { get; set; }
}
