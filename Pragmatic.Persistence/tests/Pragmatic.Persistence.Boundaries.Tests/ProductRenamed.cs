using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

/// <summary>A product's new name: the event <see cref="RenameProductAction" /> declares, rather than one the entity raises (#736).</summary>
public sealed record ProductRenamed(Guid Id, string Name, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
