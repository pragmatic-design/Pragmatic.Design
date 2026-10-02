namespace Pragmatic.Tags;

/// <summary>
///     Base class for M:N junction entities between a parent entity and Tag.
///     The SG generates <c>{Parent}Tag : EntityTagBase&lt;TEntityId&gt;</c>
///     for each entity decorated with <see cref="HasTagsAttribute" />.
///     <para>
///     Composite PK: (<c>ParentEntityId</c>, <c>TagId</c>) — no separate Id column.
///     </para>
/// </summary>
/// <typeparam name="TEntityId">The type of the parent entity's primary key.</typeparam>
public abstract class EntityTagBase<TEntityId>
    where TEntityId : notnull
{
    // ── Composite Key ──────────────────────────────────────────────────

    /// <summary>FK to the parent entity.</summary>
    public TEntityId ParentEntityId { get; set; } = default!;

    /// <summary>FK to the Tag entity.</summary>
    public Guid TagId { get; set; }

    // ── Audit ───────────────────────────────────────────────────────────

    /// <summary>When this tag was applied to the entity.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Who applied this tag.</summary>
    public string? AddedBy { get; set; }
}
