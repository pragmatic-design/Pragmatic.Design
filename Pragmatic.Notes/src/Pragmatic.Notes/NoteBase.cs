using Pragmatic.Persistence.Entity;

namespace Pragmatic.Notes;

/// <summary>
///     Base class for generated note entities.
///     The SG creates <c>{Entity}Note : NoteBase&lt;TEntityId&gt;</c>
///     for each entity decorated with <c>[HasNotes&lt;TParent&gt;]</c>.
/// </summary>
/// <typeparam name="TEntityId">The type of the parent entity's primary key.</typeparam>
public abstract class NoteBase<TEntityId> : IEntity, ISoftDelete
    where TEntityId : notnull
{
    /// <summary>Note unique identifier (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>IEntity implementation.</summary>
    public Guid PersistenceId { get => Id; set => Id = value; }

    /// <summary>FK to the parent entity.</summary>
    public TEntityId ParentEntityId { get; set; } = default!;

    /// <summary>Note text content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Author identifier (always required — notes are never anonymous).</summary>
    public string AuthorId { get; set; } = string.Empty;

    /// <summary>Display name of the author at the time of writing.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Whether this note has been edited.</summary>
    public bool IsEdited { get; set; }

    /// <summary>When created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When last edited. Null if never edited.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Who last edited. Null if never edited.</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>Soft-deleted flag.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>When soft-deleted.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Who soft-deleted.</summary>
    public string? DeletedBy { get; set; }
}
