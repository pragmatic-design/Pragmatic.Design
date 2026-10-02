namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Interface for entities that support property-level change tracking.
///     Implemented automatically by the source generator when entity properties
///     have private setters and generated Set{Property} methods.
/// </summary>
/// <remarks>
///     <para>
///         Change tracking enables:
///         <list type="bullet">
///             <item>Selective entity validation (only validate modified properties)</item>
///             <item>Audit trail of which properties changed</item>
///             <item>Optimized persistence (only update changed columns)</item>
///         </list>
///     </para>
///     <para>
///         The generator enhances Set{Property} methods to record property names
///         in <see cref="ModifiedProperties" /> whenever a value changes.
///     </para>
///     <para>
///         <b>Thread safety:</b> change-tracking state (<see cref="ModifiedProperties"/>,
///         <see cref="CollectionsModified"/>) is intended for single-threaded change tracking on
///         a single entity instance. The generated backing sets are not guaranteed to be
///         thread-safe; do not mutate an entity (via Set{Property}) from multiple threads
///         concurrently, and do not enumerate these sets while another thread is mutating the
///         entity. Entities are expected to be owned by one unit of work / request scope at a time.
///     </para>
/// </remarks>
public interface IChangeTracking
{
    /// <summary>
    ///     Gets the set of property names that have been modified since the last reset.
    ///     Not thread-safe — see the interface-level remarks on threading.
    /// </summary>
    IReadOnlySet<string> ModifiedProperties { get; }

    /// <summary>
    ///     Gets the set of collection navigation names that have been modified since the last reset.
    ///     Not thread-safe — see the interface-level remarks on threading.
    /// </summary>
    IReadOnlySet<string> CollectionsModified { get; }

    /// <summary>
    ///     Resets all change tracking state, clearing modified properties and collections.
    /// </summary>
    void ResetModifiedProperties();

    /// <summary>
    ///     Gets or sets whether this entity instance is newly created (not yet persisted).
    ///     When true, all properties are considered "modified" for validation purposes.
    ///     <para>
    ///         <b>Infrastructure only.</b> Application code must not set this property after
    ///         an entity has been loaded from the database. The flag is set to <c>true</c> by
    ///         the source-generated factory/constructor and cleared by the persistence layer on
    ///         first successful save. External mutation can cause incorrect validation behaviour
    ///         (e.g. treating a loaded entity as unvalidated).
    ///     </para>
    /// </summary>
    bool IsNew { get; set; }
}
