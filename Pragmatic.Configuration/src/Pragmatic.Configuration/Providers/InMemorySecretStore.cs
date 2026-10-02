namespace Pragmatic.Configuration.Providers;

using System.Collections.Concurrent;

/// <summary>
///     Default in-memory secret store for development and testing.
///     Secrets are stored in plain text; production implementations should use a vault.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore, IWritableSecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _tenantSecrets = new();

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        _secrets.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
    {
        if (_tenantSecrets.TryGetValue(tenantId, out var dict) &&
            dict.TryGetValue(key, out var value))
        {
            return Task.FromResult<string?>(value);
        }

        // Fallback to base
        return GetSecretAsync(key, ct);
    }

    /// <summary>Sets a secret value. For testing and development use only.</summary>
    public void SetSecret(string key, string value, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (tenantId is null)
        {
            _secrets[key] = value;
        }
        else
        {
            _tenantSecrets.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, string>())[key] = value;
        }
    }

    /// <inheritdoc />
    public Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        SetSecret(key, value, tenantId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (tenantId is null)
        {
            _secrets.TryRemove(key, out _);
        }
        else if (_tenantSecrets.TryGetValue(tenantId, out var dict))
        {
            dict.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}
