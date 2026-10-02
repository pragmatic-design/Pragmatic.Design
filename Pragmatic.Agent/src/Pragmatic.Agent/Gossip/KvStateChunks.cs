using Pragmatic.Agent.KV;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     An Agent's whole KV state — entries and tombstones — as gossip updates, split into chunks that each
///     fit a datagram.
/// </summary>
/// <remarks>
///     Values are taken as stored, so <c>secret/</c> values travel as the ciphertext they are at rest,
///     exactly as a piggybacked update carries them. An entry larger than the budget travels alone.
/// </remarks>
internal static class KvStateChunks
{
    // Room for the JSON around a key and its value: property names, version, timestamp, quotes.
    private const int OverheadPerUpdate = 128;

    public static IReadOnlyList<KvUpdate[]> Of(KvStore store, int budgetBytes)
    {
        var updates = store.GetAll()
            .Select(entry => new KvUpdate
            {
                Key = entry.Key,
                Value = entry.Value,
                Version = entry.Version,
                UpdatedAt = entry.UpdatedAt,
                Owner = entry.Owner,
            })
            .Concat(store.GetTombstones().Select(tombstone => new KvUpdate
            {
                Key = tombstone.Key,
                Version = tombstone.Version,
                Deleted = true,
                UpdatedAt = tombstone.DeletedAt,
            }));

        var chunks = new List<KvUpdate[]>();
        var current = new List<KvUpdate>();
        var size = 0;
        foreach (var update in updates)
        {
            var cost = update.Key.Length + (update.Value?.Length ?? 0) + OverheadPerUpdate;
            if (current.Count > 0 && size + cost > budgetBytes)
            {
                chunks.Add([.. current]);
                current.Clear();
                size = 0;
            }

            current.Add(update);
            size += cost;
        }

        if (current.Count > 0)
            chunks.Add([.. current]);

        return chunks;
    }
}
