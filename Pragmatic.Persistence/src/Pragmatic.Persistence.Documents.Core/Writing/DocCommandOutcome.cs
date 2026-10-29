namespace Pragmatic.Persistence.Documents.Writing;

/// <summary>What became of one document a command wrote by its id, with <c>If-Match</c> on the etag of its read (Cosmos DB).</summary>
internal enum DocCommandOutcome
{
    /// <summary>Deleted or patched.</summary>
    Written,

    /// <summary>Deleted by another writer since the read: nothing to write, nothing counted.</summary>
    Gone,

    /// <summary>Changed by another writer since the read (412): read again, then written again if the chain still selects it.</summary>
    Stale,
}
