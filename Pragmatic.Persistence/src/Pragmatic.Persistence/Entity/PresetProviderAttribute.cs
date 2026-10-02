using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Specifies a preset provider for an entity.
///     Multiple providers can be applied with different orders.
/// </summary>
/// <typeparam name="TProvider">
///     The provider type implementing <see cref="IPresetProvider{TParent}" />.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PresetProviderAttribute<TProvider> : Attribute
    where TProvider : class, IPresetProvider
{
    /// <summary>
    ///     Execution order when multiple providers exist. Lower values execute first.
    /// </summary>
    public int Order { get; init; }
}
