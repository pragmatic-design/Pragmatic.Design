namespace Pragmatic.FeatureFlags;

/// <summary>
///     Default <see cref="IFeatureFlags"/>: pairs the registered <see cref="IFeatureFlagStore"/> with the
///     ambient <see cref="IFeatureFlagContextProvider"/>, if one is registered.
/// </summary>
/// <param name="store">The store that holds the flag definitions and evaluates them.</param>
/// <param name="contextProvider">
///     Resolves the evaluation context from ambient state. Optional: without it every evaluation uses
///     <see cref="FeatureFlagContext.Empty"/>, so targeting rules never match — which is the correct
///     conservative answer for an app that has not told us who the caller is.
/// </param>
internal sealed class ContextualFeatureFlags(
    IFeatureFlagStore store,
    IFeatureFlagContextProvider? contextProvider) : IFeatureFlags
{
    public async Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
        => contextProvider is null
            ? FeatureFlagContext.Empty
            : await contextProvider.GetContextAsync(ct).ConfigureAwait(false);

    public async Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default)
    {
        var context = await GetContextAsync(ct).ConfigureAwait(false);
        return await store.IsEnabledAsync(flagName, context, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsEnabledAsync<TFlag>(CancellationToken ct = default)
        where TFlag : IFeatureFlag
        => await IsEnabledAsync(TFlag.Name, ct).ConfigureAwait(false);
}
