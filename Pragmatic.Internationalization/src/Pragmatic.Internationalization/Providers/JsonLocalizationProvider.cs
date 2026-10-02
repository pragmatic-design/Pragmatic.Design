using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Internationalization.Diagnostics;
using Pragmatic.Internationalization.Types;
using Pragmatic.Telemetry.Conventions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     Localization provider that loads translations from JSON files.
///     JSON format supports both simple strings and plural forms.
/// </summary>
/// <remarks>
///     Expected JSON format:
///     <code>
/// {
///   "Welcome": "Welcome, {name}!",
///   "Items": {
///     "zero": "No items",
///     "one": "1 item",
///     "other": "{count} items"
///   },
///   "Errors": {
///     "NotFound": "{entity} not found"
///   }
/// }
/// </code>
/// </remarks>
public sealed partial class JsonLocalizationProvider : ILocalizationProvider, IDisposable
{

    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, CultureData> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Cultures we already looked up and found no file for. Prevents a filesystem
    // stat on every request for an unknown (or hostile) culture token.
    private readonly ConcurrentDictionary<string, byte> _missingCultures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _culturesLock = new();
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<JsonLocalizationProvider>? _logger;
    private readonly HashSet<string> _supportedCultures = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _watchForChanges;
    private bool _disposed;
    private FileSystemWatcher? _watcher;

    /// <summary>
    ///     Creates a JSON provider that loads from the specified directory.
    /// </summary>
    /// <param name="basePath">Path to the translations directory.</param>
    /// <param name="watchForChanges">Enable hot reload of translation files.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public JsonLocalizationProvider(string basePath, bool watchForChanges = false,
        ILogger<JsonLocalizationProvider>? logger = null)
    {
        ThrowIfNull(basePath);
        _basePath = basePath;
        _watchForChanges = watchForChanges;
        _logger = logger;

        if (_logger is not null)
            LogInitializing(_logger, basePath);
        LoadAllCultures();
        if (_logger is not null)
            LogInitialized(_logger, _supportedCultures.Count, basePath);

        if (watchForChanges && Directory.Exists(basePath))
        {
            SetupFileWatcher();
            if (_logger is not null)
                LogFileWatcherEnabled(_logger);
        }
    }

    /// <summary>
    ///     Disposes the file watcher if enabled.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var timer in _debounceTimers.Values)
            timer.Dispose();
        _debounceTimers.Clear();

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Deleted -= OnFileChanged;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <inheritdoc />
    public string? GetString(string key, string culture)
    {
        var data = GetCultureData(culture);
        return data?.Strings.TryGetValue(key, out var value) == true ? value : null;
    }

    /// <inheritdoc />
    public PluralString? GetPlural(string key, string culture)
    {
        var data = GetCultureData(culture);
        return data?.Plurals.TryGetValue(key, out var value) == true ? value : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll(string culture)
    {
        var data = GetCultureData(culture);
        if (data is null) return new Dictionary<string, string>();
        return data.Strings as IReadOnlyDictionary<string, string>
               ?? new Dictionary<string, string>(data.Strings);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture)
    {
        var data = GetCultureData(culture);
        if (data is null) return new Dictionary<string, PluralString>();
        return data.Plurals as IReadOnlyDictionary<string, PluralString>
               ?? new Dictionary<string, PluralString>(data.Plurals);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedCultures
    {
        get
        {
            lock (_culturesLock)
            {
                return _supportedCultures.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <summary>
    ///     Reloads all translations from disk.
    /// </summary>
    public void Reload()
    {
        _cache.Clear();
        _missingCultures.Clear();
        lock (_culturesLock)
        {
            _supportedCultures.Clear();
        }

        LoadAllCultures();
    }

    /// <summary>
    ///     Reloads translations for a specific culture.
    /// </summary>
    public void Reload(string culture)
    {
        _cache.TryRemove(culture, out _);
        _missingCultures.TryRemove(culture, out _);
        LoadCulture(culture);
    }

    private void LoadAllCultures()
    {
        if (!Directory.Exists(_basePath))
            return;

        foreach (var file in Directory.GetFiles(_basePath, "*.json"))
        {
            var culture = Path.GetFileNameWithoutExtension(file);
            LoadCulture(culture);
        }
    }

    private void LoadCulture(string culture)
    {
        // Reject anything that is not a plausible culture token before it reaches the
        // filesystem. Blocks path traversal (`../`, absolute paths) and confines probing
        // to well-formed codes such as "en" or "pt-BR".
        if (!IsValidCultureToken(culture))
        {
            _missingCultures.TryAdd(culture, 0);
            return;
        }

        var filePath = Path.Combine(_basePath, $"{culture}.json");
        if (!File.Exists(filePath))
        {
            _missingCultures.TryAdd(culture, 0);
            if (_logger is not null)
                LogFileNotFound(_logger, culture, filePath);
            return;
        }

        using var activity = I18NDiagnostics.ActivitySource.StartActivity("Localization.LoadCulture");
        activity?.SetTag(I18NTags.LocalizationCulture, culture);
        activity?.SetTag(I18NTags.FilePath, filePath);

        try
        {
            var json = File.ReadAllText(filePath);
            var data = ParseJson(json);
            _cache[culture] = data;
            _missingCultures.TryRemove(culture, out _);

            lock (_culturesLock)
            {
                _supportedCultures.Add(culture); // HashSet handles duplicates
            }

            activity?.SetTag(I18NTags.StringsCount, data.Strings.Count);
            activity?.SetTag(I18NTags.PluralsCount, data.Plurals.Count);
            if (_logger is not null)
                LogCultureLoaded(_logger, culture, data.Strings.Count, data.Plurals.Count);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (_logger is not null)
                LogJsonParseError(_logger, culture, filePath, ex);
        }
        catch (IOException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (_logger is not null)
                LogFileReadError(_logger, culture, filePath, ex);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (_logger is not null)
                LogUnexpectedError(_logger, culture, filePath, ex);
        }
    }

    /// <summary>
    ///     The translations for a culture, falling back to its language when it has no file of its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Without the fallback the miss is silent: a host configured with <c>en-US</c> and
    ///         shipping <c>en.json</c> would resolve <b>nothing</b> — not an error, not an empty file,
    ///         just null all the way out to a caller reading the default English text in every language.
    ///         .NET's own <c>ResourceManager</c> walks the same chain, one subtag at a time.
    ///     </para>
    ///     <para>
    ///         The exact file has already had its chance above, so this never overrides a more specific
    ///         translation an application deliberately shipped. The result is cached under the
    ///         <em>requested</em> key, so the walk happens once.
    ///     </para>
    /// </remarks>
    private CultureData? GetCultureData(string culture)
    {
        if (_cache.TryGetValue(culture, out var data))
            return data;

        // Negative cache: already looked up and no file exists → don't hit the disk again.
        if (_missingCultures.ContainsKey(culture))
            return null;

        // Try to load if not cached
        LoadCulture(culture);
        if (_cache.TryGetValue(culture, out data))
            return data;

        // One subtag at a time — zh-Hant-CN asks zh-Hant before zh — because a script is a narrower
        // answer than a language and skipping straight to the language would discard it.
        var lastSubtag = culture.LastIndexOf('-');
        if (lastSubtag <= 0)
            return null;

        var parent = GetCultureData(culture.Substring(0, lastSubtag));
        if (parent is null)
            return null;

        // Under the key that was asked for: the next lookup hits the cache above rather than walking
        // again, and it also steps over the negative entry LoadCulture just wrote for this culture.
        _cache[culture] = parent;
        return parent;
    }

    // A culture token is at most a few short subtags of ASCII letters/digits joined by
    // '-' (e.g. "en", "pt-BR", "zh-Hant-CN"). Anything else — separators, dots, spaces —
    // cannot name a translation file and is rejected before touching the filesystem.
    private static bool IsValidCultureToken(string culture)
    {
        if (string.IsNullOrEmpty(culture) || culture.Length > 32)
            return false;

        foreach (var c in culture)
        {
            var ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-';
            if (!ok)
                return false;
        }

        return true;
    }

    // LoggerMessage source-generated methods (zero allocation)

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initializing JsonLocalizationProvider from path: {BasePath}")]
    private static partial void LogInitializing(ILogger logger, string basePath);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "JsonLocalizationProvider loaded {CultureCount} cultures from {BasePath}")]
    private static partial void LogInitialized(ILogger logger, int cultureCount, string basePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "File watcher enabled for hot reload")]
    private static partial void LogFileWatcherEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Translation file not found for culture '{Culture}' at path: {FilePath}")]
    private static partial void LogFileNotFound(ILogger logger, string culture, string filePath);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Loaded translations for culture '{Culture}': {StringCount} strings, {PluralCount} plurals")]
    private static partial void LogCultureLoaded(ILogger logger, string culture, int stringCount, int pluralCount);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to parse JSON translations for culture '{Culture}' from file: {FilePath}")]
    private static partial void LogJsonParseError(ILogger logger, string culture, string filePath, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to read translation file for culture '{Culture}' from path: {FilePath}")]
    private static partial void LogFileReadError(ILogger logger, string culture, string filePath, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Unexpected error loading translations for culture '{Culture}' from file: {FilePath}")]
    private static partial void LogUnexpectedError(ILogger logger, string culture, string filePath, Exception ex);
}