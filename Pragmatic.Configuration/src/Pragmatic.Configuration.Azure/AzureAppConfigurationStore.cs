using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Configuration.Azure;

/// <summary>
///     IConfigurationStore backed by Azure App Configuration.
///     Uses label-based environment overlay and tenant scoping.
/// </summary>
internal sealed partial class AzureAppConfigurationStore(
    ConfigurationClient client,
    IOptions<AzureConfigurationOptions> options,
    ILogger<AzureAppConfigurationStore> logger)
    : IConfigurationStore
{
    private readonly AzureConfigurationOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> _cache = new();

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        => await GetInternalAsync(key, label: null, ct).ConfigureAwait(false);

    public async Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => await GetInternalAsync(key, AzureKeyConventions.ToTenantLabel(tenantId), ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => await GetSectionInternalAsync(prefix, label: null, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
        => await GetSectionInternalAsync(prefix, AzureKeyConventions.ToTenantLabel(tenantId), ct).ConfigureAwait(false);

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        var azureKey = AzureKeyConventions.ToAppConfigKey(_options.KeyPrefix, key);
        var label = tenantId is not null ? AzureKeyConventions.ToTenantLabel(tenantId) : null;

        var setting = new ConfigurationSetting(azureKey, value, label);
        await client.SetConfigurationSettingAsync(setting, cancellationToken: ct).ConfigureAwait(false);

        // Invalidate cache
        var cacheKey = BuildCacheKey(azureKey, label);
        _cache.TryRemove(cacheKey, out _);

        LogAppConfigSet(key, label ?? "(none)");
    }

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        var azureKey = AzureKeyConventions.ToAppConfigKey(_options.KeyPrefix, key);
        var label = tenantId is not null ? AzureKeyConventions.ToTenantLabel(tenantId) : null;

        try
        {
            await client.DeleteConfigurationSettingAsync(azureKey, label, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Deleting a missing key is a no-op success (idempotent), consistent with the Get path.
        }

        var cacheKey = BuildCacheKey(azureKey, label);
        _cache.TryRemove(cacheKey, out _);
    }

    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Azure App Configuration uses a sentinel key to signal global refresh rather than per-key filtering.
        // keyPattern is not applied here — any sentinel change triggers a full reload.
        // This is intentional: Azure App Configuration does not support server-side push per key-prefix,
        // so callers should treat any emitted change as "reload all".
        // Azure App Configuration change detection via sentinel key polling
        var sentinelKey = _options.SentinelKey;
        // Initialize on first read so any change that occurred before the first poll is detected.
        bool firstPoll = true;
        string? lastSentinelValue = null;

        while (!ct.IsCancellationRequested)
        {
            var cancelled = false;
            try
            {
                await Task.Delay(_options.CacheExpiration, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            if (cancelled)
                yield break;

            string? currentSentinel = null;
            var pollFailed = false;
            try
            {
                var response = await client.GetConfigurationSettingAsync(sentinelKey, cancellationToken: ct).ConfigureAwait(false);
                currentSentinel = response.Value.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Sentinel doesn't exist yet — no changes
            }
            catch (RequestFailedException ex)
            {
                // Transient failure (throttling 429, auth 401/403, 5xx): log and keep polling instead
                // of letting the exception terminate the watch stream and silently disable hot-reload.
                LogSentinelPollFailed(ex);
                pollFailed = true;
            }

            if (pollFailed)
                continue;

            if (firstPoll)
            {
                // Establish baseline on first poll; don't emit a spurious change event.
                lastSentinelValue = currentSentinel;
                firstPoll = false;
            }
            else if (currentSentinel != lastSentinelValue)
            {
                // Sentinel changed — clear cache and signal change
                _cache.Clear();

                yield return new ConfigurationChange(
                    sentinelKey,
                    lastSentinelValue,
                    currentSentinel,
                    TenantId: null,
                    DateTimeOffset.UtcNow);

                lastSentinelValue = currentSentinel;
            }
        }
    }

    private async Task<string?> GetInternalAsync(string key, string? label, CancellationToken ct)
    {
        var azureKey = AzureKeyConventions.ToAppConfigKey(_options.KeyPrefix, key);
        var cacheKey = BuildCacheKey(azureKey, label);

        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Value;

        try
        {
            var response = await client.GetConfigurationSettingAsync(azureKey, label, ct).ConfigureAwait(false);
            var value = response.Value.Value;

            _cache[cacheKey] = (value, DateTimeOffset.UtcNow.Add(_options.CacheExpiration));
            return value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> GetSectionInternalAsync(
        string prefix, string? label, CancellationToken ct)
    {
        var keyFilter = AzureKeyConventions.ToAppConfigKeyFilter(_options.KeyPrefix, prefix);

        var selector = new SettingSelector { KeyFilter = keyFilter, LabelFilter = label ?? SettingSelector.Any };

        var result = new Dictionary<string, string>();

        await foreach (var setting in client.GetConfigurationSettingsAsync(selector, ct).ConfigureAwait(false))
        {
            var originalKey = AzureKeyConventions.FromAppConfigKey(_options.KeyPrefix, setting.Key);
            result[originalKey] = setting.Value;
        }

        return result;
    }

    private static string BuildCacheKey(string key, string? label)
        => label is null ? key : $"{key}|{label}";

    [LoggerMessage(Level = LogLevel.Debug, Message = "Azure App Configuration set: {Key} (label: {Label})")]
    private partial void LogAppConfigSet(string key, string label);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Azure App Configuration sentinel poll failed — skipping this cycle, watch continues")]
    private partial void LogSentinelPollFailed(RequestFailedException ex);
}
