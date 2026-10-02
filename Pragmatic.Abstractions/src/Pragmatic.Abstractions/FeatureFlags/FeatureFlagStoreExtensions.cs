namespace Pragmatic.FeatureFlags;

/// <summary>
///     Generic extension methods for <see cref="IFeatureFlagStore"/>.
///     Enables strongly-typed flag evaluation: <c>store.IsEnabledAsync&lt;MyFlag&gt;(ctx, ct)</c>.
/// </summary>
public static class FeatureFlagStoreExtensions
{
    /// <param name="store">The feature flag store.</param>
    extension(IFeatureFlagStore store)
    {
        /// <summary>Evaluates a typed flag without context (uses defaults).</summary>
        /// <typeparam name="TFlag">The flag type whose <c>Name</c> identifies the flag in the store.</typeparam>
        /// <param name="ct">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the flag is enabled; otherwise <c>false</c>.</returns>
        public Task<bool> IsEnabledAsync<TFlag>(CancellationToken ct = default)
            where TFlag : IFeatureFlag
            => store.IsEnabledAsync(TFlag.Name, ct);

        /// <summary>Evaluates a typed flag with targeting context.</summary>
        /// <typeparam name="TFlag">The flag type whose <c>Name</c> identifies the flag in the store.</typeparam>
        /// <param name="context">Targeting context (tenant, user, properties) used to evaluate rules.</param>
        /// <param name="ct">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the flag is enabled for the given context; otherwise <c>false</c>.</returns>
        public Task<bool> IsEnabledAsync<TFlag>(FeatureFlagContext context, CancellationToken ct = default)
            where TFlag : IFeatureFlag
            => store.IsEnabledAsync(TFlag.Name, context, ct);

        /// <summary>Gets the full definition of a typed flag.</summary>
        /// <typeparam name="TFlag">The flag type whose <c>Name</c> identifies the flag in the store.</typeparam>
        /// <param name="ct">Token to cancel the operation.</param>
        /// <returns>The flag definition, or <c>null</c> if no flag with that name exists.</returns>
        public Task<FeatureFlagDefinition?> GetDefinitionAsync<TFlag>(CancellationToken ct = default)
            where TFlag : IFeatureFlag
            => store.GetDefinitionAsync(TFlag.Name, ct);
    }
}
