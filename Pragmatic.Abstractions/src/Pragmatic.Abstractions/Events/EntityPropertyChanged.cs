namespace Pragmatic.Events;

/// <summary>
///     Domain event raised when an entity property changes.
///     Used by cascade handlers to propagate updates to dependent entities.
/// </summary>
/// <typeparam name="TEntity">The entity type whose property changed.</typeparam>
/// <remarks>
///     <b>Sensitive data:</b> <see cref="OldValue"/> and <see cref="NewValue"/> carry the raw,
///     boxed property values and may therefore contain PII or secrets (e.g. email, phone, token).
///     Persisters, loggers, and handlers must treat these payloads as potentially sensitive — do
///     not log them verbatim, and apply masking/retention rules when persisting an audit trail.
/// </remarks>
public sealed record EntityPropertyChanged<TEntity> : IDomainEvent
    where TEntity : class
{
    /// <summary>
    ///     The ID of the entity whose property changed.
    /// </summary>
    public required object EntityId { get; init; }

    /// <summary>
    ///     The name of the property that changed.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The new value of the property (boxed).
    /// </summary>
    public object? NewValue { get; init; }

    /// <summary>
    ///     The old value of the property (boxed), if available.
    /// </summary>
    public object? OldValue { get; init; }

    /// <inheritdoc />
    /// <remarks>
    ///     Defaults to <see cref="DateTimeOffset.UtcNow"/> at record construction time.
    ///     When events are batched or replayed, pass an explicit value via the
    ///     <see cref="Create"/> factory or object-initializer syntax to preserve the
    ///     actual occurrence time rather than the (later) construction time.
    /// </remarks>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    ///     Factory method for concise event creation in generated setters.
    /// </summary>
    public static EntityPropertyChanged<TEntity> Create(
        object entityId, string propertyName, object? oldValue, object? newValue)
        => new()
        {
            EntityId = entityId,
            PropertyName = propertyName,
            OldValue = oldValue,
            NewValue = newValue
        };
}
