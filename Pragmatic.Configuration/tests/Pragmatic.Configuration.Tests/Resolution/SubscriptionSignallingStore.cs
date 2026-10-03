using System.Runtime.CompilerServices;
using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     An <see cref="InMemoryConfigurationStore" /> that says when a watcher has subscribed, so a test can
///     write after the subscription instead of guessing how long it takes. The store registers a watcher
///     synchronously on the first <c>MoveNextAsync</c>, before it waits for anything; a write before that
///     reaches no one.
/// </summary>
internal sealed class SubscriptionSignallingStore : IConfigurationStore
{
    private readonly InMemoryConfigurationStore _inner = new();

    public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string?> GetAsync(string key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default) =>
        _inner.GetAsync(key, tenantId, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default) =>
        _inner.GetSectionAsync(prefix, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string tenantId, CancellationToken ct = default) =>
        _inner.GetSectionAsync(prefix, tenantId, ct);

    public Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default) =>
        _inner.SetAsync(key, value, tenantId, ct);

    public Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default) =>
        _inner.DeleteAsync(key, tenantId, ct);

    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var changes = _inner.WatchAsync(keyPattern, ct).GetAsyncEnumerator(ct);
        var next = changes.MoveNextAsync();
        Subscribed.TrySetResult();

        while (await next)
        {
            yield return changes.Current;
            next = changes.MoveNextAsync();
        }
    }
}
