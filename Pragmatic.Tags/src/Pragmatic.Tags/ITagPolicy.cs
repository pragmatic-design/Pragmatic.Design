namespace Pragmatic.Tags;

/// <summary>
///     Hook interface for customizing tag behavior per entity type.
///     Register via DI to override default tag validation and lifecycle.
/// </summary>
/// <typeparam name="TEntityId">The parent entity's ID type.</typeparam>
public interface ITagPolicy<TEntityId>
    where TEntityId : notnull
{
    /// <summary>
    ///     Validates whether a tag value is allowed for this entity type.
    ///     Called before creating a new tag (when <see cref="HasTagsAttribute.AllowCustom"/> is true).
    ///     Default: allows all non-empty values.
    /// </summary>
    Task<bool> IsAllowedAsync(string tagValue, CancellationToken ct = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(tagValue));

    /// <summary>
    ///     Normalizes a tag value before storage and matching.
    ///     Default: trims whitespace and converts to lowercase.
    /// </summary>
    string Normalize(string tagValue)
        => tagValue.Trim().ToLowerInvariant();

    /// <summary>
    ///     Called after a tag is successfully added to an entity.
    /// </summary>
    Task OnTagAddedAsync(TEntityId entityId, Guid tagId, string tagValue, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    ///     Called after a tag is removed from an entity.
    /// </summary>
    Task OnTagRemovedAsync(TEntityId entityId, Guid tagId, string tagValue, CancellationToken ct = default)
        => Task.CompletedTask;
}
