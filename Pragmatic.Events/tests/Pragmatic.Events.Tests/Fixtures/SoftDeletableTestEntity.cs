using Pragmatic.Persistence.Entity;

namespace Pragmatic.Events.Tests.Fixtures;

/// <summary>
///     A soft-deletable entity that records every lifecycle transition it is asked to raise, so a test
///     can assert which <see cref="EntityLifecycle" /> the interceptor derived from the tracked state.
/// </summary>
public sealed class SoftDeletableTestEntity : IRaisesLifecycleEvents, ISoftDelete
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    /// <summary>The lifecycles the interceptor asked this entity to raise, in order.</summary>
    public List<EntityLifecycle> RaisedLifecycles { get; } = [];

    public void RaiseLifecycleEvents(EntityLifecycle lifecycle) => RaisedLifecycles.Add(lifecycle);
}
