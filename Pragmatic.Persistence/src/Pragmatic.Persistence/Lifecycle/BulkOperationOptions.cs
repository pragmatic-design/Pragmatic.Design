namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Options for bulk/batch operations (event deferral, and — reserved — chunked SaveChanges).
/// </summary>
public sealed record BulkOperationOptions
{
    /// <summary>
    ///     Number of entities per SaveChanges call. 0 = save all at once.
    /// </summary>
    /// <remarks>
    ///     RESERVED / NOT YET HONORED: the batch pipeline accumulates all entities and
    ///     issues a single SaveChanges regardless of this value. Real chunking changes the transactional
    ///     model (partial commits) and is a deliberate design decision left for a future release — the
    ///     value is validated and round-tripped but does not yet split the save.
    /// </remarks>
    public int ChunkSize { get; init; }

    /// <summary>
    ///     Whether each chunk should be wrapped in its own transaction.
    ///     When false, the entire batch shares one transaction.
    /// </summary>
    /// <remarks>RESERVED / NOT YET HONORED — see <see cref="ChunkSize"/>.</remarks>
    public bool ChunkAsTransaction { get; init; }
}
