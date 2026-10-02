using Pragmatic.Events;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Simple entity with Guid ID for CRUD testing.
/// </summary>
public class TestProduct : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    /// <summary>Optional category FK for Include testing.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Navigation to category.</summary>
    public TestCategory? Category { get; set; }
}

/// <summary>
///     Category entity for testing Include/navigation loading.
/// </summary>
public class TestCategory : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<TestProduct> Products { get; set; } = new List<TestProduct>();
}

/// <summary>
///     Entity with soft-delete support for testing query filters.
/// </summary>
public class TestOrder : IEntity, ISoftDelete
{
    public Guid PersistenceId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>
///     Entity with audit fields for testing auto-population.
/// </summary>
public class TestCustomer : IEntity, IAuditable
{
    public Guid PersistenceId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
///     Entity with ShortGuid converter column for round-trip testing.
/// </summary>
public class TestDocument : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>
    ///     External ID stored as ShortGuid in the database.
    /// </summary>
    public Guid ExternalId { get; set; }
}

/// <summary>
///     Entity with OpaqueId converter column for round-trip testing.
/// </summary>
public class TestInvoice : IEntity
{
    public Guid PersistenceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>
    ///     Sequential number stored as OpaqueId string in the database.
    /// </summary>
    public long SequenceNumber { get; set; }
}

/// <summary>
///     Entity that raises domain events, written through a repository rather than a mutation.
/// </summary>
/// <remarks>
///     The shape the unit of work has to hand over to the batch: nothing in the pipeline drains this
///     one, so before the hand-over its events reached the interceptor and were dispatched mid-save.
/// </remarks>
public class TestAnnouncement : IEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public Guid PersistenceId { get; set; }
    public string Headline { get; set; } = string.Empty;

    /// <inheritdoc />
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    /// <summary>Raises <see cref="TestAnnouncementPublished" /> on this announcement.</summary>
    public void Publish() => _events.Add(new TestAnnouncementPublished(PersistenceId, DateTimeOffset.UtcNow));

    /// <inheritdoc />
    public void ClearDomainEvents() => _events.Clear();
}

/// <summary>Raised when an announcement is published.</summary>
public sealed record TestAnnouncementPublished(Guid AnnouncementId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
///     Entity with concurrency token for optimistic concurrency testing.
///     Uses uint RowVersion for InMemory/SQLite provider (Generic strategy).
/// </summary>
public class TestArticle : IEntity
{
    public Guid PersistenceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
}
