namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     A row could not be deleted because another row still references it, and the relation between
///     them restricts the delete.
/// </summary>
/// <remarks>
///     <para>
///         Status code 409 (Conflict): the request is well formed, and the state it meets refuses it —
///         the same answer a duplicate key gets. It is not <see cref="DbConstraintError" />, the 400 for a
///         write that names something missing: there the request is wrong, here something else holds the
///         row, and the caller can act on which.
///     </para>
///     <para>
///         <see cref="EntityType" /> and <see cref="UsedBy" /> are entity names read from the model, not
///         schema names: they are what the application declared, and safe on the wire.
///     </para>
/// </remarks>
public sealed record DbInUseError : Error
{
    /// <inheritdoc />
    public override string Code => "ENTITY_IN_USE";

    /// <inheritdoc />
    public override int StatusCode => 409;

    /// <inheritdoc />
    public override string Title => "In use";

    /// <inheritdoc />
    public override string? Description => UsedBy is null
        ? $"The {EntityType} is still referenced and cannot be deleted."
        : $"The {EntityType} is still used by a {UsedBy} and cannot be deleted.";

    /// <summary>The entity the delete was refused for.</summary>
    public required string EntityType { get; init; }

    /// <summary>The entity whose rows still reference it, when the relation could be told.</summary>
    public string? UsedBy { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        extensions["entityType"] = EntityType;
        if (UsedBy is not null) extensions["usedBy"] = UsedBy;
    }
}
