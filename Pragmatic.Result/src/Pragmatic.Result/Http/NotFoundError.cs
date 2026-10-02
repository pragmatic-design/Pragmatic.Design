namespace Pragmatic.Result.Http;

/// <summary>
///     Represents an error when a requested resource cannot be found.
///     Maps to HTTP 404 Not Found.
/// </summary>
/// <remarks>
///     <para>
///         <b>Usage:</b>
///         <code>
/// // Simple creation
/// return NotFoundError.Create("User", userId);
/// 
/// // With entity name
/// return NotFoundError.For("User", userId);
/// </code>
///     </para>
/// </remarks>
public sealed record NotFoundError : Error
{
    /// <inheritdoc />
    public override string Code => "NOT_FOUND";

    /// <inheritdoc />
    public override int StatusCode => 404;

    /// <inheritdoc />
    public override string Title => "Not Found";

    /// <summary>
    ///     Gets the type of entity that was not found (e.g., "User", "Order").
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    ///     Gets the identifier of the entity that was not found.
    /// </summary>
    public string? EntityId { get; init; }

    /// <inheritdoc />
    /// <remarks>
    ///     Built on each read, not cached: a cache is a field, a record compares every field, and a
    ///     <c>with</c> copy would carry the original's. It is read at the serialization boundary.
    /// </remarks>
    public override IReadOnlyDictionary<string, object>? Parameters => BuildParameters();

    private IReadOnlyDictionary<string, object>? BuildParameters()
        => EntityType is null
            ? null
            : new Dictionary<string, object>
            {
                ["entityType"] = EntityType,
                ["entityId"] = EntityId ?? "unknown"
            };

    /// <summary>
    ///     Creates a NotFoundError with the specified entity type and optional identifier.
    /// </summary>
    public static NotFoundError Create(string entityType, string? entityId = null)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return new NotFoundError { EntityType = entityType, EntityId = entityId };
    }

    /// <summary>
    ///     Creates a NotFoundError with a typed identifier.
    /// </summary>
    public static NotFoundError Create<TId>(string entityType, TId entityId)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return new NotFoundError { EntityType = entityType, EntityId = entityId?.ToString() };
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (EntityType is not null) extensions["entityType"] = EntityType;
        if (EntityId is not null) extensions["entityId"] = EntityId;
    }

    /// <summary>
    ///     Creates a NotFoundError with an entity name and optional string identifier.
    /// </summary>
    /// <param name="entityName">The entity type name (e.g., "User", "Order").</param>
    /// <param name="entityId">The identifier of the entity that was not found.</param>
    public static NotFoundError For(string entityName, string? entityId = null)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        return new NotFoundError { EntityType = entityName, EntityId = entityId };
    }

    /// <summary>
    ///     Creates a NotFoundError with an entity name and typed identifier.
    /// </summary>
    /// <param name="entityName">The entity type name (e.g., "User", "Order").</param>
    /// <param name="entityId">The identifier of the entity that was not found.</param>
    public static NotFoundError For<TId>(string entityName, TId entityId)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        return new NotFoundError { EntityType = entityName, EntityId = entityId?.ToString() };
    }

    /// <summary>
    ///     Creates a NotFoundError naming every identifier of a list that names no entity — comma-separated
    ///     in <see cref="EntityId" />, in the order given.
    /// </summary>
    /// <remarks>
    ///     One error for all of them, not one for the first: a caller told of one missing key fixes it and
    ///     meets the next. With a single identifier it is the error <see cref="For{TId}" /> gives.
    /// </remarks>
    /// <param name="entityName">The entity type name (e.g., "User", "Order").</param>
    /// <param name="entityIds">The identifiers that name no entity.</param>
    public static NotFoundError ForAll<TId>(string entityName, IEnumerable<TId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        ArgumentNullException.ThrowIfNull(entityIds);
        return new NotFoundError { EntityType = entityName, EntityId = string.Join(", ", entityIds) };
    }
}