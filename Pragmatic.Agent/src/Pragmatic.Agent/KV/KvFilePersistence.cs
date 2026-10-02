using System.Text.Json;

namespace Pragmatic.Agent.KV;

/// <summary>
///     Periodic file persistence for the KV store.
///     Flushes all entries to a JSON file every N seconds.
///     Loads from file on startup.
/// </summary>
internal sealed class KvFilePersistence(KvStore store, string filePath, TimeSpan? flushInterval = null)
    : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TimeSpan _flushInterval = flushInterval ?? TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _cts = new();
    private Task? _flushTask;

    /// <summary>Loads entries from disk into the KV store.</summary>
    public void Load()
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<KvEntryDto>>(json, JsonOptions);
            if (entries is null)
                return;

            // An ephemeral entry lives as long as a client connection, and no client of the previous
            // process is connected to this one. One another Agent still owns comes back from
            // it by anti-entropy; one owned by an Agent that died meanwhile must not come back at all.
            store.LoadBulk(entries
                .Where(e => e.Owner is null)
                .Select(e => new KvEntry(e.Key, e.Value, e.Version, e.UpdatedAt)));
        }
        catch (Exception ex)
        {
            // Log but don't crash — agent can start with empty KV
            AgentLogger.Error("KV", $"Failed to load KV from {filePath}: {ex.Message}");
        }
    }

    /// <summary>Starts the periodic flush background task.</summary>
    public void StartPeriodicFlush()
    {
        _flushTask = FlushLoopAsync(_cts.Token);
    }

    /// <summary>Flushes current KV state to disk immediately.</summary>
    public void FlushNow()
    {
        try
        {
            var entries = store.GetAll()
                .Select(e => new KvEntryDto
                {
                    Key = e.Key,
                    Value = e.Value,
                    Version = e.Version,
                    UpdatedAt = e.UpdatedAt,
                    Owner = e.Owner,
                })
                .ToList();

            var directory = Path.GetDirectoryName(filePath);
            if (directory is not null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Atomic write: write to temp file, then rename
            var tempPath = filePath + ".tmp";
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AgentLogger.Error("KV", $"Failed to flush KV to {filePath}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        // Wait for the background loop to stop without blocking synchronously:
        // use a short timeout fire-and-forget pattern safe on any sync-context.
        // The loop exits on OperationCanceledException so this is fast in practice.
        try
        {
            // Allow up to 2 seconds for the flush loop to observe cancellation cleanly.
            _flushTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException) { /* faulted or cancelled — expected */ }

        // Final flush on shutdown
        FlushNow();
        _cts.Dispose();
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_flushInterval, ct).ConfigureAwait(false);
                FlushNow();
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private sealed class KvEntryDto
    {
        public string Key { get; init; } = "";
        public string Value { get; init; } = "";
        public long Version { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public string? Owner { get; init; }
    }
}
