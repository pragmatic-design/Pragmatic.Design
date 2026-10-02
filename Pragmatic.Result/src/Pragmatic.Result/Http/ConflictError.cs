namespace Pragmatic.Result.Http;

/// <summary>
///     Represents a conflict error when an operation cannot be completed due to a conflict.
///     Maps to HTTP 409 Conflict.
/// </summary>
/// <remarks>
///     <para>
///         <b>Common scenarios:</b>
///         <list type="bullet">
///             <item>Entity already exists (duplicate key)</item>
///             <item>Optimistic concurrency conflict (stale data)</item>
///             <item>Domain rule violation causing state conflict</item>
///         </list>
///     </para>
///     <para>
///         <b>Usage:</b>
///         <code>
/// return ConflictError.AlreadyExists("User", email);
/// return ConflictError.ConcurrencyConflict("Order", orderId);
/// return ConflictError.DuplicateKey("Email", email);
/// </code>
///     </para>
/// </remarks>
public sealed record ConflictError : Error
{
    /// <inheritdoc />
    public override string Code => "CONFLICT";

    /// <inheritdoc />
    public override int StatusCode => 409;

    /// <inheritdoc />
    public override string Title => "Conflict";

    /// <summary>
    ///     Gets the type of entity involved in the conflict.
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    ///     Gets the identifier of the entity involved in the conflict.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    ///     Gets the reason for the conflict.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the field that caused the conflict (for duplicate key errors).
    /// </summary>
    public string? Field { get; init; }

    /// <inheritdoc />
    public override string MessageKey => Reason switch
    {
        "AlreadyExists" => "error.conflict.already_exists",
        "ConcurrencyConflict" => "error.conflict.concurrency",
        "DuplicateKey" => "error.conflict.duplicate_key",
        _ => base.MessageKey
    };

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object>? Parameters => BuildParameters();

    private IReadOnlyDictionary<string, object>? BuildParameters()
    {
        var dict = new Dictionary<string, object>();
        if (EntityType is not null)
            dict["entityType"] = EntityType;
        if (EntityId is not null)
            dict["entityId"] = EntityId;
        if (Field is not null)
            dict["field"] = Field;
        if (Reason is not null)
            dict["reason"] = Reason;

        return dict.Count > 0 ? dict : null;
    }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (EntityType is not null) extensions["entityType"] = EntityType;
        if (EntityId is not null) extensions["entityId"] = EntityId;
        if (Reason is not null) extensions["reason"] = Reason;
        if (Field is not null) extensions["field"] = Field;
    }

    /// <summary>
    ///     Creates a ConflictError for an entity that already exists.
    /// </summary>
    public static ConflictError AlreadyExists(string entityType, string? entityId = null)
    {
        return new ConflictError { EntityType = entityType, EntityId = entityId, Reason = "AlreadyExists" };
    }

    /// <summary>
    ///     Creates a ConflictError for an entity that already exists (typed identifier).
    /// </summary>
    public static ConflictError AlreadyExists<TId>(string entityType, TId entityId)
    {
        return new ConflictError { EntityType = entityType, EntityId = entityId?.ToString(), Reason = "AlreadyExists" };
    }

    /// <summary>
    ///     Creates a ConflictError for an optimistic concurrency conflict.
    /// </summary>
    public static ConflictError ConcurrencyConflict(string entityType, string? entityId = null)
    {
        return new ConflictError { EntityType = entityType, EntityId = entityId, Reason = "ConcurrencyConflict" };
    }

    /// <summary>
    ///     Creates a ConflictError for an optimistic concurrency conflict (typed identifier).
    /// </summary>
    public static ConflictError ConcurrencyConflict<TId>(string entityType, TId entityId)
    {
        return new ConflictError
        { EntityType = entityType, EntityId = entityId?.ToString(), Reason = "ConcurrencyConflict" };
    }

    /// <summary>
    ///     Creates a ConflictError for a duplicate key violation.
    /// </summary>
    public static ConflictError DuplicateKey(string field, string? value = null)
    {
        return new ConflictError { Field = field, EntityId = value, Reason = "DuplicateKey" };
    }
}