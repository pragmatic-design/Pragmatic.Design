namespace Pragmatic.FeatureFlags;

/// <summary>
///     Evaluates feature flags against the <b>ambient</b> context, resolving it for you.
///     <para>
///         <see cref="IFeatureFlagStore"/> answers "is this flag on for <i>this</i> context" and leaves the
///         caller to build the context. That is the right contract for a store, but it means every call site
///         repeats the same two steps — resolve the context from
///         <see cref="IFeatureFlagContextProvider"/>, then pass it in. This interface is those two steps,
///         so business code asks the question directly.
///     </para>
///     <para>
///         When no <see cref="IFeatureFlagContextProvider"/> is registered, evaluation falls back to
///         <see cref="FeatureFlagContext.Empty"/> — the flag's global state and percentage rules (seeded
///         <c>"anonymous"</c>) still apply, targeting rules simply do not match.
///     </para>
/// </summary>
/// <example>
///     <code>
/// // instead of:
/// var context = await contextProvider.GetContextAsync(ct);
/// var on = await store.IsEnabledAsync&lt;EarlyCheckIn&gt;(context, ct);
///
/// // write:
/// var on = await flags.IsEnabledAsync&lt;EarlyCheckIn&gt;(ct);
/// </code>
/// </example>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IFeatureFlags
{
    /// <summary>Evaluates a flag by name against the ambient context.</summary>
    Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default);

    /// <summary>Evaluates a strongly-typed flag against the ambient context.</summary>
    /// <typeparam name="TFlag">The flag type whose <c>Name</c> identifies the flag in the store.</typeparam>
    Task<bool> IsEnabledAsync<TFlag>(CancellationToken ct = default) where TFlag : IFeatureFlag;

    /// <summary>The context that would be used for evaluation right now.</summary>
    /// <remarks>Useful when several flags are evaluated together, or to log what a decision was based on.</remarks>
    Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default);
}
