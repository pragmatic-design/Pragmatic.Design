using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Pragmatic.Logging.Privacy.Audit.Storage;

/// <summary>
/// File system implementation of audit storage.
/// Stores audit entries in JSON Lines format with optional compression.
/// </summary>
public sealed class FileSystemAuditStorage : IAuditStorage, IAuditQuery
{
    private readonly FileSystemAuditOptions _options;
    private volatile bool _disposed;

    public FileSystemAuditStorage(FileSystemAuditOptions? options = null)
    {
        _options = options ?? new FileSystemAuditOptions();

        EnsureDirectoryExists();
    }

    public async Task StoreEntriesAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (entries.Count == 0)
            return;

        var entriesByDate = entries.GroupBy(e => e.Timestamp.Date);

        foreach (var group in entriesByDate)
        {
            var filePath = GetFilePathForDate(group.Key);
            await AppendEntriesToFileAsync(filePath, group, cancellationToken);
        }

        // Trigger compression for old files if enabled
        if (_options.CompressOldFiles)
        {
            _ = Task.Run(CompressOldFilesAsync, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> GetEntriesAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entries = new List<AuditEntry>();
        var currentDate = from.Date;

        // Inclusive of until.Date: the day named by `until` has its own date-partitioned file,
        // and an `until` timestamp later than 00:00:00 selects entries within that day. Using
        // `<` here silently skipped that entire file (off-by-one), dropping same-day entries.
        while (currentDate <= until.Date)
        {
            var filePath = GetFilePathForDate(currentDate);
            var compressedPath = filePath + ".gz";

            // Try compressed file first, then uncompressed
            var pathToRead = File.Exists(compressedPath) ? compressedPath :
                           File.Exists(filePath) ? filePath : null;

            if (pathToRead != null)
            {
                var dateEntries = await ReadEntriesFromFileAsync(pathToRead, cancellationToken);
                entries.AddRange(dateEntries.Where(e => e.Timestamp >= from && e.Timestamp < until));
            }

            currentDate = currentDate.AddDays(1);
        }

        return entries;
    }

    public async Task<long> GetEntryCountAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default)
    {
        var entries = await GetEntriesAsync(from, until, cancellationToken);
        return entries.Count;
    }

    public async Task<long> DeleteEntriesAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long deletedCount = 0;
        var cutoffDate = before.Date;

        if (!Directory.Exists(_options.BasePath))
            return 0;

        var files = Directory.GetFiles(_options.BasePath, "audit-*.json*")
            .Concat(Directory.GetFiles(_options.BasePath, "audit-*.jsonl*"));

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                fileName = Path.GetFileNameWithoutExtension(fileName);

            if (TryExtractDateFromFileName(fileName, out var fileDate) && fileDate < cutoffDate)
            {
                // Count entries before deletion. Stream-count lines instead of deserializing
                // every entry into memory — we only need the count, not the objects.
                try
                {
                    deletedCount += await CountEntriesInFileAsync(file, cancellationToken);
                    File.Delete(file);
                }
                catch
                {
                    // Continue with other files if one fails
                }
            }
        }

        return deletedCount;
    }

    public async Task<AuditStorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new AuditStorageHealthResult { IsHealthy = true };

        try
        {
            // Check directory accessibility
            EnsureDirectoryExists();

            // Test write capability
            var testFile = Path.Combine(_options.BasePath, $"health-check-{Guid.NewGuid()}.tmp");
            await File.WriteAllTextAsync(testFile, "health-check", cancellationToken);
            File.Delete(testFile);

            // Check disk space
            var drive = new DriveInfo(Path.GetPathRoot(_options.BasePath) ?? _options.BasePath);
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

            result.Details["FreeSpaceGB"] = freeSpaceGB;
            result.Details["BasePath"] = _options.BasePath;

            if (freeSpaceGB < 1.0) // Less than 1GB free space
            {
                result.IsHealthy = false;
                result.ErrorMessage = $"Low disk space: {freeSpaceGB:F2} GB available";
            }
        }
        catch (Exception ex)
        {
            result.IsHealthy = false;
            result.ErrorMessage = ex.Message;
        }

        result.ResponseTimeMs = stopwatch.Elapsed.TotalMilliseconds;
        return result;
    }

    public async Task<AuditQueryResult> QueryAsync(AuditQueryBuilder query, CancellationToken cancellationToken = default)
    {
        var specification = query.Build();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // For file system storage, we need to scan date-based files
        // This is a simplified implementation - real-world might need indexing
        var allEntries = new List<AuditEntry>();

        // Determine date range from filters
        var (fromDate, untilDate) = ExtractDateRangeFromFilters(specification.Filters);
        fromDate ??= DateTime.UtcNow.AddDays(-30); // Default to last 30 days
        untilDate ??= DateTime.UtcNow;

        var entries = await GetEntriesAsync(fromDate.Value, untilDate.Value, cancellationToken);

        // Apply filters
        var filteredEntries = entries.Where(entry =>
            specification.Filters.All(filter => filter.Matches(entry))).ToList();

        // Apply sorting
        var sortedEntries = ApplySorting(filteredEntries, specification.SortCriteria);

        // Apply pagination
        var totalCount = sortedEntries.Count;
        if (specification.Skip.HasValue)
            sortedEntries = sortedEntries.Skip(specification.Skip.Value).ToList();
        if (specification.Take.HasValue)
            sortedEntries = sortedEntries.Take(specification.Take.Value).ToList();

        return new AuditQueryResult
        {
            Entries = sortedEntries,
            TotalCount = totalCount,
            HasMore = specification.Take.HasValue && totalCount > (specification.Skip ?? 0) + specification.Take.Value,
            ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    public async Task<AuditStatistics> GetStatisticsAsync(DateTime from, DateTime until, CancellationToken cancellationToken = default)
    {
        var entries = await GetEntriesAsync(from, until, cancellationToken);

        var stats = new AuditStatistics
        {
            TotalEntries = entries.Count,
            CoverageTimeSpan = until - from
        };

        // Group by event type
        stats.EntriesByEventType = entries
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // Group by compliance standard
        stats.EntriesByComplianceStandard = entries
            .GroupBy(e => e.ComplianceStandard)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // Group by severity
        stats.EntriesBySeverity = entries
            .GroupBy(e => e.Severity)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // Top users by activity
        stats.TopUsersByActivity = entries
            .Where(e => !string.IsNullOrEmpty(e.UserId))
            .GroupBy(e => e.UserId!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        return stats;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_options.BasePath))
        {
            Directory.CreateDirectory(_options.BasePath);
        }
    }

    private string GetFilePathForDate(DateTime date)
    {
        var fileName = string.Format(CultureInfo.InvariantCulture, _options.FilePattern, date);
        return Path.Combine(_options.BasePath, fileName);
    }

    private async Task AppendEntriesToFileAsync(string filePath, IEnumerable<AuditEntry> entries, CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(fileStream);

        foreach (var entry in entries)
        {
            var json = JsonSerializer.Serialize(entry, AuditJsonContext.Default.AuditEntry);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
    }

    private async Task<List<AuditEntry>> ReadEntriesFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var entries = new List<AuditEntry>();

        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            stream = new GZipStream(stream, CompressionMode.Decompress);
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEntry);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                    // Skip malformed entries
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Counts non-empty JSON-Lines records in a (optionally gzipped) file by streaming, without
    /// deserializing each entry. Used by deletion/retention paths that only need a count.
    /// </summary>
    private static async Task<long> CountEntriesInFileAsync(string filePath, CancellationToken cancellationToken)
    {
        long count = 0;

        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            stream = new GZipStream(stream, CompressionMode.Decompress);
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    count++;
            }
        }

        return count;
    }

    private async Task CompressOldFilesAsync()
    {
        if (!Directory.Exists(_options.BasePath))
            return;

        var cutoffDate = DateTime.UtcNow - _options.CompressionAge;
        var files = Directory.GetFiles(_options.BasePath, "audit-*.jsonl")
            .Where(f => !f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (TryExtractDateFromFileName(fileName, out var fileDate) && fileDate < cutoffDate.Date)
            {
                await CompressFileAsync(file);
            }
        }
    }

    private static async Task CompressFileAsync(string filePath)
    {
        var compressedPath = filePath + ".gz";

        using var originalStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var compressedStream = new FileStream(compressedPath, FileMode.Create, FileAccess.Write);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress);

        await originalStream.CopyToAsync(gzipStream);

        File.Delete(filePath);
    }

    private static bool TryExtractDateFromFileName(string fileName, out DateTime date)
    {
        // Extract date from "audit-2025-01-09" format
        if (fileName.StartsWith("audit-", StringComparison.OrdinalIgnoreCase) && fileName.Length >= 15)
        {
            var dateStr = fileName.Substring(6, 10); // "2025-01-09"
            return DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        date = default;
        return false;
    }

    private static (DateTime? from, DateTime? until) ExtractDateRangeFromFilters(IAuditFilter[] filters)
    {
        DateTime? from = null;
        DateTime? until = null;

        foreach (var filter in filters.OfType<TimestampRangeFilter>())
        {
            // This would require exposing the range from TimestampRangeFilter
            // For now, return nulls to scan all files
        }

        return (from, until);
    }

    private static List<AuditEntry> ApplySorting(List<AuditEntry> entries, AuditSortCriteria[] sortCriteria)
    {
        if (sortCriteria.Length == 0)
            return entries.OrderBy(e => e.Timestamp).ToList();

        IOrderedEnumerable<AuditEntry>? orderedEntries = null;

        foreach (var criteria in sortCriteria)
        {
            if (orderedEntries == null)
            {
                orderedEntries = criteria.Field switch
                {
                    AuditSortField.Timestamp => criteria.Ascending ? entries.OrderBy(e => e.Timestamp) : entries.OrderByDescending(e => e.Timestamp),
                    AuditSortField.Severity => criteria.Ascending ? entries.OrderBy(e => e.Severity) : entries.OrderByDescending(e => e.Severity),
                    AuditSortField.EventType => criteria.Ascending ? entries.OrderBy(e => e.EventType) : entries.OrderByDescending(e => e.EventType),
                    AuditSortField.UserId => criteria.Ascending ? entries.OrderBy(e => e.UserId) : entries.OrderByDescending(e => e.UserId),
                    _ => entries.OrderBy(e => e.Timestamp)
                };
            }
            else
            {
                orderedEntries = criteria.Field switch
                {
                    AuditSortField.Timestamp => criteria.Ascending ? orderedEntries.ThenBy(e => e.Timestamp) : orderedEntries.ThenByDescending(e => e.Timestamp),
                    AuditSortField.Severity => criteria.Ascending ? orderedEntries.ThenBy(e => e.Severity) : orderedEntries.ThenByDescending(e => e.Severity),
                    AuditSortField.EventType => criteria.Ascending ? orderedEntries.ThenBy(e => e.EventType) : orderedEntries.ThenByDescending(e => e.EventType),
                    AuditSortField.UserId => criteria.Ascending ? orderedEntries.ThenBy(e => e.UserId) : orderedEntries.ThenByDescending(e => e.UserId),
                    _ => orderedEntries.ThenBy(e => e.Timestamp)
                };
            }
        }

        return orderedEntries?.ToList() ?? entries;
    }
}

/// <summary>
/// Configuration options for file system audit storage.
/// </summary>
public sealed class FileSystemAuditOptions
{
    /// <summary>Gets or sets the base path for storing audit files.</summary>
    public string BasePath { get; set; } = "./audit-logs";

    /// <summary>Gets or sets the file name pattern (date will be formatted into {0}).</summary>
    public string FilePattern { get; set; } = "audit-{0:yyyy-MM-dd}.jsonl";

    /// <summary>Gets or sets whether to compress old files.</summary>
    public bool CompressOldFiles { get; set; } = true;

    /// <summary>Gets or sets the age after which files should be compressed.</summary>
    public TimeSpan CompressionAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Gets or sets the maximum file size in MB before rotation.</summary>
    public int MaxFileSizeMB { get; set; } = 100;

    /// <summary>Gets or sets the retention period for audit files.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(90);
}