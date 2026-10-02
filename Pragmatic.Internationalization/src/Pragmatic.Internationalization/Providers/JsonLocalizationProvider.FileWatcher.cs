using Microsoft.Extensions.Logging;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     File-watching / hot-reload concern of <see cref="JsonLocalizationProvider"/>: wires a
///     <see cref="FileSystemWatcher"/> to debounced per-culture reloads.
/// </summary>
public sealed partial class JsonLocalizationProvider
{
    private void SetupFileWatcher()
    {
        _watcher = new FileSystemWatcher(_basePath, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var culture = Path.GetFileNameWithoutExtension(e.Name);
        if (string.IsNullOrEmpty(culture))
            return;

        if (_logger is not null)
            LogFileChanged(_logger, culture, e.ChangeType.ToString());

        // Debounce: reset timer on each event to avoid reading while file is being written
        var timer = _debounceTimers.GetOrAdd(culture, static (c, self) =>
            new Timer(self.OnDebounceElapsed, c, Timeout.Infinite, Timeout.Infinite), this);

        timer.Change(200, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        var culture = (string)state!;
        try
        {
            Reload(culture);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LogUnexpectedError(_logger, culture, Path.Combine(_basePath, $"{culture}.json"), ex);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var oldCulture = Path.GetFileNameWithoutExtension(e.OldName);
        var newCulture = Path.GetFileNameWithoutExtension(e.Name);

        if (_logger is not null)
            LogFileRenamed(_logger, oldCulture, newCulture);

        if (!string.IsNullOrEmpty(oldCulture))
        {
            _cache.TryRemove(oldCulture, out _);
            // Dispose and remove the debounce timer for the old culture to avoid resource leaks
            if (_debounceTimers.TryRemove(oldCulture, out var oldTimer))
                oldTimer.Dispose();
            lock (_culturesLock)
            {
                _supportedCultures.Remove(oldCulture);
            }
        }

        if (!string.IsNullOrEmpty(newCulture))
            Reload(newCulture);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Translation file changed, reloading culture '{Culture}' (event: {ChangeType})")]
    private static partial void LogFileChanged(ILogger logger, string culture, string changeType);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Translation file renamed from '{OldCulture}' to '{NewCulture}'")]
    private static partial void LogFileRenamed(ILogger logger, string? oldCulture, string? newCulture);
}
