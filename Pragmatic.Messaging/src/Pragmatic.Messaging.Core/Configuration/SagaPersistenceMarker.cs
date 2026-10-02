namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Carries the DbContext <see cref="System.Type"/> that persists an <c>[EnableSagaPersistence]</c>
///     boundary's sagas. Registered (keyed by the DbContext's full name) from the host-generated DbContext
///     registration — where the concrete DbContext type is available — and resolved lazily by the
///     boundary-library saga registration, which cannot reference that host-generated type at compile time.
/// </summary>
/// <remarks>
///     Lazy resolution (in the repository factory, not at registration time) is what makes saga EF
///     persistence independent of DI registration order: the host registers the marker whenever it runs,
///     and the saga repository reads it only when first resolved.
/// </remarks>
public sealed class SagaPersistenceMarker
{
    /// <summary>The DbContext type that hosts the saga tables (<c>__SagaInstances</c>/<c>__SagaSteps</c>).</summary>
    public System.Type DbContextType { get; }

    public SagaPersistenceMarker(System.Type dbContextType)
        => DbContextType = dbContextType ?? throw new System.ArgumentNullException(nameof(dbContextType));
}
