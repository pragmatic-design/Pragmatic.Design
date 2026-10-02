using Pragmatic.Identity;

namespace Pragmatic.Authorization.Providers;

/// <summary>
///     Aggregates permissions from multiple <see cref="IPermissionProvider"/> instances
///     in order, merging results with union semantics.
/// </summary>
public sealed class CompositePermissionProvider : IPermissionProvider
{
    private readonly IReadOnlyList<IPermissionProvider> _inner;

    public CompositePermissionProvider(IEnumerable<IPermissionProvider> providers)
    {
        _inner = providers.Where(p => p is not CompositePermissionProvider)
            .OrderBy(p => p.Order)
            .ToList();
    }

    /// <inheritdoc />
    public int Order => -1;

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default)
    {
        // Resolve all providers in parallel to reduce I/O latency.
        var results = await Task.WhenAll(
            _inner.Select(p => p.ResolvePermissionsAsync(user, ct).AsTask()))
            .ConfigureAwait(false);

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permissions in results)
            foreach (var p in permissions)
                merged.Add(p);

        return merged;
    }
}
