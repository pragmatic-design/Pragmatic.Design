namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Non-generic marker for preset providers. Used to constrain the
///     <c>PresetProviderAttribute&lt;TProvider&gt;</c> so only valid provider types compile.
///     Implement <see cref="IPresetProvider{TParent}" /> instead of this interface directly.
/// </summary>
public interface IPresetProvider
{
}

/// <summary>
///     Creates preset child entities when a parent entity is created.
///     Registered via <c>[PresetProvider&lt;TProvider&gt;]</c> on the parent entity.
/// </summary>
/// <typeparam name="TParent">The parent entity type.</typeparam>
public interface IPresetProvider<in TParent> : IPresetProvider where TParent : class
{
    /// <summary>
    ///     Creates preset entities based on the parent entity state.
    /// </summary>
    /// <param name="parent">The parent entity being created.</param>
    /// <param name="context">The lifecycle context (clock, user, tenant).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Entities to add alongside the parent.</returns>
    Task<IReadOnlyList<object>> CreatePresetsAsync(TParent parent, LifecycleContext context, CancellationToken ct);
}
